```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.0.1 (25A362) [Darwin 25.0.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host]     : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a


```
| Method                 | Key       | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------------- |---------- |------------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **&#39;Driver: LINQ filter&#39;**  | **Id**        | **1,931.41 ns** | **24.270 ns** | **21.515 ns** |  **1.00** |    **0.02** | **0.5493** |      **-** |    **4656 B** |        **1.00** |
| &#39;Driver: Eq filter&#39;    | Id        |   727.86 ns |  6.952 ns |  6.503 ns |  0.38 |    0.01 | 0.2756 |      - |    2312 B |        0.50 |
| &#39;MongoFlow: key model&#39; | Id        |    78.69 ns |  0.847 ns |  0.661 ns |  0.04 |    0.00 | 0.0842 | 0.0001 |     704 B |        0.15 |
|                        |           |             |           |           |       |         |        |        |           |             |
| **&#39;Driver: LINQ filter&#39;**  | **Composite** | **4,426.44 ns** | **40.075 ns** | **35.525 ns** |  **1.00** |    **0.01** | **1.0071** |      **-** |    **8656 B** |        **1.00** |
| &#39;Driver: Eq filter&#39;    | Composite | 1,590.35 ns | 12.762 ns | 10.657 ns |  0.36 |    0.00 | 0.6294 | 0.0019 |    5272 B |        0.61 |
| &#39;MongoFlow: key model&#39; | Composite |   166.14 ns |  1.410 ns |  1.250 ns |  0.04 |    0.00 | 0.1490 | 0.0002 |    1248 B |        0.14 |
