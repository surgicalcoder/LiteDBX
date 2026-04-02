using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiteDbX.Spatial;
using Xunit;
using SpatialApi = LiteDbX.Spatial.Spatial;

namespace LiteDbX.Tests.Spatial;

public class Spatial_Tests
{
    [Fact]
    public async Task Near_Returns_Ordered_Within_Radius()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var col = db.GetCollection<Place>("p");
        await SpatialApi.EnsurePointIndex(col, x => x.Location);

        var center = new GeoPoint(48.2082, 16.3738);
        await col.Insert(new[]
        {
            new Place { Name = "A", Location = new GeoPoint(48.215, 16.355) },
            new Place { Name = "B", Location = new GeoPoint(48.185, 16.38) },
            new Place { Name = "C", Location = new GeoPoint(48.300, 16.450) }
        });

        var result = await SpatialApi.Near(col, x => x.Location, center, 5_000).ToListAsync();

        Assert.Equal(new[] { "A", "B" }, result.Select(x => x.Name));
        Assert.True(result.SequenceEqual(result.OrderBy(x => GeoMath.DistanceMeters(center, x.Location, DistanceFormula.Haversine))));
    }

    [Fact]
    public async Task BoundingBox_Crosses_Antimeridian()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var col = db.GetCollection<Place>("p");
        await SpatialApi.EnsurePointIndex(col, x => x.Location);

        await col.Insert(new Place { Name = "W", Location = new GeoPoint(0, -179.5) });
        await col.Insert(new Place { Name = "E", Location = new GeoPoint(0, 179.5) });

        var result = await SpatialApi.WithinBoundingBox(col, x => x.Location, -10, 170, 10, -170).ToListAsync();

        Assert.Contains(result, p => p.Name == "W");
        Assert.Contains(result, p => p.Name == "E");
    }

    [Theory]
    [InlineData(89.9, 0, 89.9, 90)]
    [InlineData(-89.9, 0, -89.9, -90)]
    public void GreatCircle_Is_Stable_Near_Poles(double lat1, double lon1, double lat2, double lon2)
    {
        var d = GeoMath.DistanceMeters(new GeoPoint(lat1, lon1), new GeoPoint(lat2, lon2), DistanceFormula.Haversine);
        Assert.InRange(d, 0, 100);
    }

    [Fact]
    public void GeoJson_RoundTrip_Point()
    {
        var point = new GeoPoint(48.2082, 16.3738);
        var json = GeoJson.Serialize(point);
        var back = GeoJson.Deserialize<GeoPoint>(json);

        Assert.Equal(point.Lat, back.Lat, 10);
        Assert.Equal(point.Lon, back.Lon, 10);
    }

    [Fact]
    public async Task Within_Polygon_Excludes_Hole()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var col = db.GetCollection<Place>("p");
        await SpatialApi.EnsureShapeIndex<Place, GeoPoint>(col, x => x.Location);

        var outer = new[]
        {
            new GeoPoint(48.25, 16.30),
            new GeoPoint(48.25, 16.45),
            new GeoPoint(48.15, 16.45),
            new GeoPoint(48.15, 16.30),
            new GeoPoint(48.25, 16.30)
        };

        var hole = new[]
        {
            new GeoPoint(48.22, 16.36),
            new GeoPoint(48.22, 16.39),
            new GeoPoint(48.18, 16.39),
            new GeoPoint(48.18, 16.36),
            new GeoPoint(48.22, 16.36)
        };

        var poly = new GeoPolygon(outer, new[] { hole });
        await col.Insert(new[]
        {
            new Place { Name = "Edge", Location = new GeoPoint(48.20, 16.37) },
            new Place { Name = "Outside", Location = new GeoPoint(48.30, 16.50) }
        });

        var result = await SpatialApi.Within(col, x => x.Location, poly).ToListAsync();

        Assert.DoesNotContain(result, p => p.Name == "Edge");
        Assert.DoesNotContain(result, p => p.Name == "Outside");
    }

    [Fact]
    public async Task GeoHash_Recomputes_On_Update()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var col = db.GetCollection<Place>("p");
        await SpatialApi.EnsurePointIndex(col, x => x.Location);

        var place = new Place { Name = "P", Location = new GeoPoint(48.2, 16.37) };
        await col.Insert(place);

        var h1 = (await col.FindOne(x => x.Name == "P"))._gh;

        place.Location = new GeoPoint(48.21, 16.38);
        await col.Update(place);

        var h2 = (await col.FindById(place.Id))._gh;

        Assert.NotEqual(h1, h2);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    public void Invalid_Coordinates_Throw(double lat, double lon)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPoint(lat, lon));
    }

    [Fact]
    public async Task Intersects_Line_Touches_Polygon_Edge()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var roads = db.GetCollection<Road>("roads");
        await SpatialApi.EnsureShapeIndex<Road, GeoLineString>(roads, r => r.Path);

        await roads.Insert(new Road
        {
            Name = "Cross",
            Path = new GeoLineString(new List<GeoPoint>
            {
                new GeoPoint(0.0, -1.0),
                new GeoPoint(0.0, 2.0)
            })
        });

        var square = new GeoPolygon(new List<GeoPoint>
        {
            new GeoPoint(0.5, -0.5),
            new GeoPoint(0.5, 1.5),
            new GeoPoint(-0.5, 1.5),
            new GeoPoint(-0.5, -0.5),
            new GeoPoint(0.5, -0.5)
        });

        var hits = await SpatialApi.Intersects(roads, r => r.Path, square).ToListAsync();
        Assert.NotEmpty(hits);
    }

    [Fact]
    public async Task EnsurePointIndex_Persists_Precision_Metadata()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var col = db.GetCollection<Place>("p");
        var meta = db.GetCollection<BsonDocument>("_spatial_meta");

        await SpatialApi.EnsurePointIndex(col, x => x.Location, 40);

        var docs = await meta.FindAll().ToListAsync();
        Assert.Single(docs);
        Assert.Equal(40, docs[0]["precisionBits"].AsInt32);
    }

    [Fact]
    public async Task Query_Near_Helper_Filters_Results()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var col = db.GetCollection<Place>("p");
        await SpatialApi.EnsurePointIndex(col, x => x.Location);

        await col.Insert(new[]
        {
            new Place { Name = "Center", Location = new GeoPoint(48.2082, 16.3738) },
            new Place { Name = "Far", Location = new GeoPoint(48.35, 16.7) }
        });

        var center = new GeoPoint(48.2082, 16.3738);
        var results = await col.Find(Query.Near("Location", center, 1_000)).ToListAsync();

        Assert.Single(results);
        Assert.Equal("Center", results[0].Name);
    }

    [Fact]
    public async Task Collection_Spatial_Helpers_Find_Count_And_Delete_Work()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var col = db.GetCollection<Place>("p");
        await SpatialApi.EnsurePointIndex(col, x => x.Location);

        await col.Insert(new[]
        {
            new Place { Name = "Center", Location = new GeoPoint(48.2082, 16.3738) },
            new Place { Name = "Near", Location = new GeoPoint(48.2090, 16.3740) },
            new Place { Name = "Far", Location = new GeoPoint(48.35, 16.7) }
        });

        var center = new GeoPoint(48.2082, 16.3738);

        var found = await col.FindNear(x => x.Location, center, 500).ToListAsync();
        var count = await col.CountNear(x => x.Location, center, 500);
        var deleted = await col.DeleteNear(x => x.Location, center, 500);
        var remaining = await col.FindAll().ToListAsync();

        Assert.Equal(2, found.Count);
        Assert.Equal(2, count);
        Assert.Equal(2, deleted);
        Assert.Single(remaining);
        Assert.Equal("Far", remaining[0].Name);
    }

    [Fact]
    public async Task Collection_FindWithin_Helper_Works()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var col = db.GetCollection<Place>("p");
        await SpatialApi.EnsureShapeIndex(col, x => x.Location);

        var polygon = new GeoPolygon(new[]
        {
            new GeoPoint(48.25, 16.30),
            new GeoPoint(48.25, 16.45),
            new GeoPoint(48.15, 16.45),
            new GeoPoint(48.15, 16.30),
            new GeoPoint(48.25, 16.30)
        });

        await col.Insert(new[]
        {
            new Place { Name = "Inside", Location = new GeoPoint(48.21, 16.37) },
            new Place { Name = "Outside", Location = new GeoPoint(48.30, 16.60) }
        });

        var results = await col.FindWithin(x => x.Location, polygon).ToListAsync();

        Assert.Single(results);
        Assert.Equal("Inside", results[0].Name);
    }

    [Fact]
    public async Task Linq_Near_Uses_Spatial_Operator()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var col = db.GetCollection<Place>("p");
        await SpatialApi.EnsurePointIndex(col, x => x.Location);

        await col.Insert(new[]
        {
            new Place { Name = "Center", Location = new GeoPoint(48.2082, 16.3738) },
            new Place { Name = "Far", Location = new GeoPoint(48.35, 16.7) }
        });

        var center = new GeoPoint(48.2082, 16.3738);
        var results = await col.Query().Where(p => SpatialApi.Near(p.Location, center, 1_000)).ToList();

        Assert.Single(results);
        Assert.Equal("Center", results[0].Name);
    }

    [Fact]
    public async Task Linq_Within_Uses_Spatial_Operator()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var col = db.GetCollection<Place>("p");
        await SpatialApi.EnsureShapeIndex<Place, GeoPoint>(col, x => x.Location);

        var polygon = new GeoPolygon(new[]
        {
            new GeoPoint(48.25, 16.30),
            new GeoPoint(48.25, 16.45),
            new GeoPoint(48.15, 16.45),
            new GeoPoint(48.15, 16.30),
            new GeoPoint(48.25, 16.30)
        });

        await col.Insert(new[]
        {
            new Place { Name = "Inside", Location = new GeoPoint(48.21, 16.37) },
            new Place { Name = "Outside", Location = new GeoPoint(48.30, 16.60) }
        });

        var results = await col.Query().Where(p => SpatialApi.Within(p.Location, polygon)).ToList();

        Assert.Single(results);
        Assert.Equal("Inside", results[0].Name);
    }

    private class Place
    {
        public ObjectId Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public GeoPoint Location { get; set; } = new(0, 0);
        internal long _gh { get; set; }
        internal double[] _mbb { get; set; } = Array.Empty<double>();
    }

    private class Road
    {
        public ObjectId Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public GeoLineString Path { get; set; } = new(new[] { new GeoPoint(0, 0), new GeoPoint(0, 0.1) });
        internal double[] _mbb { get; set; } = Array.Empty<double>();
    }
}

