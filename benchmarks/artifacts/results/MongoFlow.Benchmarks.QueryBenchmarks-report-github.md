```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.0.1 (25A362) [Darwin 25.0.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host]     : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a


```
| Method                  | Filter     | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------ |----------- |---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **&#39;Driver: AsQueryable&#39;**   | **None**       | **110.2 ns** |  **0.98 ns** |  **0.87 ns** |  **1.00** |    **0.01** | **0.0411** |     **344 B** |        **1.00** |
| &#39;MongoFlow: QueryAsync&#39; | None       | 121.2 ns |  0.75 ns |  0.67 ns |  1.10 |    0.01 | 0.0381 |     320 B |        0.93 |
|                         |            |          |          |          |       |         |        |           |             |
| **&#39;Driver: AsQueryable&#39;**   | **Static**     | **483.7 ns** |  **8.16 ns** |  **6.81 ns** |  **1.00** |    **0.02** | **0.1593** |    **1336 B** |        **1.00** |
| &#39;MongoFlow: QueryAsync&#39; | Static     | 342.5 ns |  6.77 ns |  5.29 ns |  0.71 |    0.01 | 0.0877 |     736 B |        0.55 |
|                         |            |          |          |          |       |         |        |           |             |
| **&#39;Driver: AsQueryable&#39;**   | **PerQuery**   | **678.5 ns** | **13.52 ns** | **27.30 ns** |  **1.00** |    **0.06** | **0.2127** |    **1784 B** |        **1.00** |
| &#39;MongoFlow: QueryAsync&#39; | PerQuery   | 991.2 ns | 11.97 ns | 11.20 ns |  1.46 |    0.06 | 0.3052 |    2616 B |        1.47 |
|                         |            |          |          |          |       |         |        |           |             |
| **&#39;Driver: AsQueryable&#39;**   | **Async**      | **597.6 ns** |  **6.27 ns** |  **5.24 ns** |  **1.00** |    **0.01** | **0.1926** |    **1616 B** |        **1.00** |
| &#39;MongoFlow: QueryAsync&#39; | Async      | 998.2 ns |  8.17 ns |  7.64 ns |  1.67 |    0.02 | 0.3052 |    2616 B |        1.62 |
|                         |            |          |          |          |       |         |        |           |             |
| **&#39;Driver: AsQueryable&#39;**   | **Tenant**     | **639.0 ns** | **10.26 ns** |  **8.57 ns** |  **1.00** |    **0.02** | **0.2127** |    **1784 B** |        **1.00** |
| &#39;MongoFlow: QueryAsync&#39; | Tenant     | 702.7 ns |  8.75 ns |  8.19 ns |  1.10 |    0.02 | 0.1984 |    1664 B |        0.93 |
|                         |            |          |          |          |       |         |        |           |             |
| **&#39;Driver: AsQueryable&#39;**   | **FeatureOff** | **108.4 ns** |  **2.16 ns** |  **2.12 ns** |  **1.00** |    **0.03** | **0.0411** |     **344 B** |        **1.00** |
| &#39;MongoFlow: QueryAsync&#39; | FeatureOff | 143.5 ns |  1.04 ns |  0.87 ns |  1.32 |    0.03 | 0.0553 |     464 B |        1.35 |
