```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.0.1 (25A362) [Darwin 25.0.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host]     : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a


```
| Method                          | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |---------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| &#39;Driver: Lookup&#39;                | 2.254 μs | 0.0341 μs | 0.0319 μs |  1.00 |    0.02 | 0.7172 |   5.88 KB |        1.00 |
| &#39;MongoFlow: join on QueryAsync&#39; | 3.797 μs | 0.0510 μs | 0.0477 μs |  1.69 |    0.03 | 0.9003 |   7.38 KB |        1.25 |
