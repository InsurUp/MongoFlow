```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.0.1 (25A362) [Darwin 25.0.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host]     : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a


```
| Method                  | Filter     | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------ |----------- |----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **&#39;Driver: AsQueryable&#39;**   | **None**       | **106.31 ns** |  **0.605 ns** |  **0.472 ns** |  **1.00** |    **0.01** | **0.0411** |     **344 B** |        **1.00** |
| &#39;MongoFlow: QueryAsync&#39; | None       |  27.11 ns |  0.247 ns |  0.275 ns |  0.26 |    0.00 | 0.0086 |      72 B |        0.21 |
|                         |            |           |           |           |       |         |        |           |             |
| **&#39;Driver: AsQueryable&#39;**   | **Static**     | **474.80 ns** |  **4.344 ns** |  **4.064 ns** |  **1.00** |    **0.01** | **0.1593** |    **1336 B** |        **1.00** |
| &#39;MongoFlow: QueryAsync&#39; | Static     | 236.87 ns |  4.279 ns |  7.932 ns |  0.50 |    0.02 | 0.0582 |     488 B |        0.37 |
|                         |            |           |           |           |       |         |        |           |             |
| **&#39;Driver: AsQueryable&#39;**   | **PerQuery**   | **631.50 ns** |  **5.119 ns** |  **4.274 ns** |  **1.00** |    **0.01** | **0.2127** |    **1784 B** |        **1.00** |
| &#39;MongoFlow: QueryAsync&#39; | PerQuery   | 893.07 ns | 15.404 ns | 14.409 ns |  1.41 |    0.02 | 0.2823 |    2368 B |        1.33 |
|                         |            |           |           |           |       |         |        |           |             |
| **&#39;Driver: AsQueryable&#39;**   | **Async**      | **584.72 ns** |  **4.969 ns** |  **4.149 ns** |  **1.00** |    **0.01** | **0.1926** |    **1616 B** |        **1.00** |
| &#39;MongoFlow: QueryAsync&#39; | Async      | 913.31 ns | 13.206 ns | 12.353 ns |  1.56 |    0.02 | 0.2823 |    2368 B |        1.47 |
|                         |            |           |           |           |       |         |        |           |             |
| **&#39;Driver: AsQueryable&#39;**   | **Tenant**     | **714.72 ns** | **13.651 ns** | **17.750 ns** |  **1.00** |    **0.03** | **0.2289** |    **1944 B** |        **1.00** |
| &#39;MongoFlow: QueryAsync&#39; | Tenant     | 581.73 ns |  7.376 ns |  9.847 ns |  0.81 |    0.02 | 0.1688 |    1416 B |        0.73 |
|                         |            |           |           |           |       |         |        |           |             |
| **&#39;Driver: AsQueryable&#39;**   | **FeatureOff** | **108.43 ns** |  **0.673 ns** |  **0.562 ns** |  **1.00** |    **0.01** | **0.0411** |     **344 B** |        **1.00** |
| &#39;MongoFlow: QueryAsync&#39; | FeatureOff |  49.61 ns |  0.557 ns |  0.465 ns |  0.46 |    0.00 | 0.0268 |     224 B |        0.65 |
