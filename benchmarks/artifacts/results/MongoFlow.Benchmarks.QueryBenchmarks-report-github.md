```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.0.1 (25A362) [Darwin 25.0.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host]     : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a


```
| Method                  | Filter     | Mean     | Error    | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------ |----------- |---------:|---------:|--------:|------:|--------:|-------:|----------:|------------:|
| **&#39;Driver: AsQueryable&#39;**   | **None**       | **105.7 ns** |  **0.59 ns** | **0.52 ns** |  **1.00** |    **0.01** | **0.0411** |     **344 B** |        **1.00** |
| &#39;MongoFlow: QueryAsync&#39; | None       | 117.6 ns |  0.87 ns | 0.68 ns |  1.11 |    0.01 | 0.0381 |     320 B |        0.93 |
|                         |            |          |          |         |       |         |        |           |             |
| **&#39;Driver: AsQueryable&#39;**   | **Static**     | **471.0 ns** |  **3.60 ns** | **3.19 ns** |  **1.00** |    **0.01** | **0.1593** |    **1336 B** |        **1.00** |
| &#39;MongoFlow: QueryAsync&#39; | Static     | 325.0 ns |  2.06 ns | 1.93 ns |  0.69 |    0.01 | 0.0877 |     736 B |        0.55 |
|                         |            |          |          |         |       |         |        |           |             |
| **&#39;Driver: AsQueryable&#39;**   | **PerQuery**   | **617.6 ns** |  **4.42 ns** | **3.92 ns** |  **1.00** |    **0.01** | **0.2127** |    **1784 B** |        **1.00** |
| &#39;MongoFlow: QueryAsync&#39; | PerQuery   | 957.8 ns |  9.41 ns | 8.80 ns |  1.55 |    0.02 | 0.3090 |    2616 B |        1.47 |
|                         |            |          |          |         |       |         |        |           |             |
| **&#39;Driver: AsQueryable&#39;**   | **Async**      | **573.1 ns** |  **3.63 ns** | **3.03 ns** |  **1.00** |    **0.01** | **0.1926** |    **1616 B** |        **1.00** |
| &#39;MongoFlow: QueryAsync&#39; | Async      | 958.6 ns | 10.21 ns | 7.97 ns |  1.67 |    0.02 | 0.3109 |    2616 B |        1.62 |
|                         |            |          |          |         |       |         |        |           |             |
| **&#39;Driver: AsQueryable&#39;**   | **FeatureOff** | **106.6 ns** |  **1.10 ns** | **0.98 ns** |  **1.00** |    **0.01** | **0.0411** |     **344 B** |        **1.00** |
| &#39;MongoFlow: QueryAsync&#39; | FeatureOff | 144.1 ns |  2.73 ns | 4.26 ns |  1.35 |    0.04 | 0.0553 |     464 B |        1.35 |
