using BenchmarkDotNet.Running;
using Moongazing.OrionGuard.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
