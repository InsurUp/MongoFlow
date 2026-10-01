```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.0.1 (25A362) [Darwin 25.0.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host]     : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a
  Job-CNUJVU : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a

InvocationCount=1  UnrollFactor=1  

```
| Method                          | Count | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------------------------------- |------ |----------:|----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **&#39;Driver: client bulk write&#39;**     | **1**     |  **3.156 ms** | **0.6262 ms** | **1.8463 ms** |  **2.502 ms** |  **1.33** |    **1.06** |  **45.48 KB** |        **1.00** |
| &#39;MongoFlow: save&#39;               | 1     |  3.556 ms | 0.5368 ms | 1.5744 ms |  3.156 ms |  1.50 |    1.01 |  47.63 KB |        1.05 |
| &#39;MongoFlow: save with features&#39; | 1     |  3.956 ms | 0.4957 ms | 1.4304 ms |  3.844 ms |  1.67 |    1.02 |  48.79 KB |        1.07 |
|                                 |       |           |           |           |           |       |         |           |             |
| **&#39;Driver: client bulk write&#39;**     | **100**   |  **4.646 ms** | **0.4158 ms** | **1.1796 ms** |  **4.665 ms** |  **1.07** |    **0.40** | **119.08 KB** |        **1.00** |
| &#39;MongoFlow: save&#39;               | 100   |  4.848 ms | 0.4627 ms | 1.3499 ms |  4.708 ms |  1.12 |    0.44 |  128.2 KB |        1.08 |
| &#39;MongoFlow: save with features&#39; | 100   |  4.434 ms | 0.3287 ms | 0.9326 ms |  4.534 ms |  1.02 |    0.35 | 132.45 KB |        1.11 |
|                                 |       |           |           |           |           |       |         |           |             |
| **&#39;Driver: client bulk write&#39;**     | **1000**  | **10.121 ms** | **1.0291 ms** | **3.0342 ms** | **10.169 ms** |  **1.10** |    **0.48** | **766.41 KB** |        **1.00** |
| &#39;MongoFlow: save&#39;               | 1000  | 10.938 ms | 0.7616 ms | 2.2456 ms | 10.617 ms |  1.18 |    0.45 | 839.19 KB |        1.09 |
| &#39;MongoFlow: save with features&#39; | 1000  | 10.677 ms | 0.8341 ms | 2.4592 ms |  9.832 ms |  1.16 |    0.45 | 871.56 KB |        1.14 |
