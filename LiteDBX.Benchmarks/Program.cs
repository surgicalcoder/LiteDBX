using System;
using System.Collections.Generic;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using LiteDbX.Encryption.Gcm;

namespace LiteDbX.Benchmarks
{
    internal class Program
    {
        private enum BenchmarkProfile
        {
            Smoke,
            Full
        }

        private static void Main(string[] args)
        {
            GcmEncryptionRegistration.Register();

            var (profile, benchmarkArgs) = ParseProfile(args);

            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(benchmarkArgs, CreateConfig(profile));
        }

        private static IConfig CreateConfig(BenchmarkProfile profile)
        {
            var job = CreateJob(profile);

            return DefaultConfig.Instance
                                //.With(new BenchmarkDotNet.Filters.AnyCategoriesFilter(new[] { Benchmarks.Constants.Categories.GENERAL }))
                                //.AddFilter(new BenchmarkDotNet.Filters.AnyCategoriesFilter([Benchmarks.Constants.Categories.GENERAL]))
                                .AddJob(job)
                                /*.With(Job.Default.With(MonoRuntime.Default)
                                    .With(Jit.Llvm)
                                    .With(new[] {new MonoArgument("--optimize=inline")})
                                    .WithGcForce(true))*/
                                .AddDiagnoser(MemoryDiagnoser.Default)
                                .KeepBenchmarkFiles();
        }

        private static Job CreateJob(BenchmarkProfile profile)
        {
            var job = Job.Default.WithRuntime(CoreRuntime.Core10_0)
                                 .WithJit(Jit.RyuJit)
                                 .WithGcForce(true);

            return profile == BenchmarkProfile.Smoke
                ? job.WithId("Smoke")
                     .WithLaunchCount(1)
                     .WithWarmupCount(2)
                     .WithIterationCount(8)
                : job.WithId("Full")
                     .WithLaunchCount(1)
                     .WithWarmupCount(6)
                     .WithIterationCount(20);
        }

        private static (BenchmarkProfile Profile, string[] RemainingArgs) ParseProfile(string[] args)
        {
            var profile = BenchmarkProfile.Full;
            var remainingArgs = new List<string>(args.Length);

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                if (string.Equals(arg, "--profile", StringComparison.OrdinalIgnoreCase))
                {
                    if (i == args.Length - 1)
                    {
                        throw new ArgumentException("Missing benchmark profile value after --profile. Expected 'smoke' or 'full'.");
                    }

                    profile = ParseProfileValue(args[++i]);
                    continue;
                }

                remainingArgs.Add(arg);
            }

            return (profile, remainingArgs.ToArray());
        }

        private static BenchmarkProfile ParseProfileValue(string value)
        {
            return value.ToLowerInvariant() switch
            {
                "smoke" => BenchmarkProfile.Smoke,
                "full" => BenchmarkProfile.Full,
                _ => throw new ArgumentException($"Unsupported benchmark profile '{value}'. Expected 'smoke' or 'full'.")
            };
        }
    }
}