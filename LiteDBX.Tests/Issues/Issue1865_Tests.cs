using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue1865_Tests
{
    [Fact]
    public async Task Incluced_document_types_should_be_reald()
    {
        BsonMapper.Global.Entity<Point>().DbRef(p => p.Project, "activity");
        BsonMapper.Global.Entity<Point>().DbRef(p => p.Parent, "activity");
        BsonMapper.Global.Entity<Project>().DbRef(p => p.Points, "activity");

        await using var _database = new LiteDatabase(":memory:");
        var projectsCol = _database.GetCollection<Project>("activity");
        var pointsCol = _database.GetCollection<Point>("activity");

        var project = new Project { Name = "Project" };
        var point1 = new Point { Parent = project, Project = project, Name = "Point 1", Start = DateTime.Now, End = DateTime.Now.AddDays(2) };
        var point2 = new Point { Parent = point1, Project = project, Name = "Point 2", Start = DateTime.Now, End = DateTime.Now.AddDays(2) };

        project.Points.Add(point1);
        project.Points.Add(point2);

        await pointsCol.Insert(point1);
        await pointsCol.Insert(point2);
        await projectsCol.Insert(project);


        var p1 = await pointsCol
            .FindById(point1.Id);
        Assert.Equal(typeof(Project), p1.Parent.GetType());
        Assert.Equal(typeof(Project), p1.Project.GetType());

        var p2 = await pointsCol
            .FindById(point2.Id);
        Assert.Equal(typeof(Point), p2.Parent.GetType());
        Assert.Equal(typeof(Project), p2.Project.GetType());

        var prj = await projectsCol
            .FindById(project.Id);
        Assert.Equal(typeof(Point), prj.Points[0].GetType());

        p1 = await pointsCol
             .Include(p => p.Parent).Include(p => p.Project)
             .FindById(point1.Id);
        Assert.Equal(typeof(Project), p1.Parent.GetType());
        Assert.Equal(typeof(Project), p1.Project.GetType());

        p2 = await pointsCol
             .Include(p => p.Parent).Include(p => p.Project)
             .FindById(point2.Id);
        Assert.Equal(typeof(Point), p2.Parent.GetType());
        Assert.Equal(typeof(Project), p2.Project.GetType());

        prj = await projectsCol
              .Include(p => p.Points)
              .FindById(project.Id);
        Assert.Equal(typeof(Point), prj.Points[0].GetType());
    }

    public class Project : BaseEntity
    {
        public List<BaseEntity> Points { get; set; } = new();
    }

    public class Point : BaseEntity
    {
        public BaseEntity Project { get; set; }
        public BaseEntity Parent { get; set; }
        public DateTime Start { get; internal set; }
        public DateTime End { get; internal set; }
    }

    public class BaseEntity
    {
        public ObjectId Id { get; set; } = ObjectId.NewObjectId();
        public string Name { get; set; }
    }
}