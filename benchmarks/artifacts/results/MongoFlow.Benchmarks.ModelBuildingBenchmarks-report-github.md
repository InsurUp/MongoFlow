```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.0.1 (25A362) [Darwin 25.0.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host]     : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a


```
| Method                           | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|--------------------------------- |---------:|---------:|---------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| &#39;MongoFlow: vault&#39;               | 17.08 μs | 0.269 μs | 0.238 μs |  1.00 |    0.02 | 3.2349 | 1.5869 |      - |   26.5 KB |        1.00 |
| &#39;MongoFlow: vault with features&#39; | 77.76 μs | 1.066 μs | 0.997 μs |  4.55 |    0.08 | 6.7139 | 3.2959 | 0.2441 |     55 KB |        2.08 |
