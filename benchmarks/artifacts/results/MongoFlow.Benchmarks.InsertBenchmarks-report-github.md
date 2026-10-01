```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.0.1 (25A362) [Darwin 25.0.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host]     : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a
  Job-CNUJVU : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a

InvocationCount=1  UnrollFactor=1  

```
| Method                          | Count | Mean      | Error     | StdDev    | Median   | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------------------------------- |------ |----------:|----------:|----------:|---------:|------:|--------:|----------:|------------:|
| **&#39;Driver: client bulk write&#39;**     | **1**     |  **3.228 ms** | **0.4650 ms** | **1.3712 ms** | **2.886 ms** |  **1.19** |    **0.74** |  **45.48 KB** |        **1.00** |
| &#39;MongoFlow: save&#39;               | 1     |  2.658 ms | 0.3864 ms | 1.1085 ms | 2.188 ms |  0.98 |    0.60 |  47.89 KB |        1.05 |
| &#39;MongoFlow: save with features&#39; | 1     |  2.380 ms | 0.3433 ms | 0.9958 ms | 1.986 ms |  0.88 |    0.54 |  48.45 KB |        1.07 |
|                                 |       |           |           |           |          |       |         |           |             |
| **&#39;Driver: client bulk write&#39;**     | **100**   |  **4.324 ms** | **0.4098 ms** | **1.2018 ms** | **4.534 ms** |  **1.08** |    **0.44** | **119.08 KB** |        **1.00** |
| &#39;MongoFlow: save&#39;               | 100   |  4.400 ms | 0.3754 ms | 1.1011 ms | 4.471 ms |  1.10 |    0.42 | 128.45 KB |        1.08 |
| &#39;MongoFlow: save with features&#39; | 100   |  4.198 ms | 0.3498 ms | 1.0148 ms | 4.286 ms |  1.05 |    0.40 | 131.34 KB |        1.10 |
|                                 |       |           |           |           |          |       |         |           |             |
| **&#39;Driver: client bulk write&#39;**     | **1000**  |  **9.652 ms** | **0.8312 ms** | **2.3981 ms** | **9.656 ms** |  **1.06** |    **0.38** | **766.41 KB** |        **1.00** |
| &#39;MongoFlow: save&#39;               | 1000  |  9.843 ms | 0.9335 ms | 2.7229 ms | 9.699 ms |  1.08 |    0.41 | 839.45 KB |        1.10 |
| &#39;MongoFlow: save with features&#39; | 1000  | 10.238 ms | 0.9934 ms | 2.8821 ms | 9.917 ms |  1.13 |    0.43 | 863.02 KB |        1.13 |
