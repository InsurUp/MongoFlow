```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.0.1 (25A362) [Darwin 25.0.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host]     : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a
  Job-CNUJVU : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a

InvocationCount=1  UnrollFactor=1  

```
| Method                      | Count | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0       | Gen1      | Gen2      | Allocated   | Alloc Ratio |
|---------------------------- |------ |-----------:|----------:|----------:|------:|--------:|-----------:|----------:|----------:|------------:|------------:|
| **&#39;Driver: client bulk write&#39;** | **100**   |   **7.008 ms** | **0.2212 ms** | **0.6091 ms** |  **1.01** |    **0.12** |          **-** |         **-** |         **-** |   **855.79 KB** |        **1.00** |
| &#39;MongoFlow: DeleteByKey&#39;    | 100   |   6.764 ms | 0.3250 ms | 0.8898 ms |  0.97 |    0.15 |          - |         - |         - |   497.23 KB |        0.58 |
|                             |       |            |           |           |       |         |            |           |           |             |             |
| **&#39;Driver: client bulk write&#39;** | **10000** | **298.729 ms** | **5.9626 ms** | **9.6285 ms** |  **1.00** |    **0.04** | **10000.0000** | **3000.0000** | **1000.0000** | **79141.54 KB** |        **1.00** |
| &#39;MongoFlow: DeleteByKey&#39;    | 10000 | 313.561 ms | 5.9930 ms | 6.1544 ms |  1.05 |    0.04 |  6000.0000 | 3000.0000 | 1000.0000 | 44618.09 KB |        0.56 |
