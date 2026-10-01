```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.0.1 (25A362) [Darwin 25.0.0]
Apple M4 Max, 1 CPU, 16 logical and 16 physical cores
.NET SDK 11.0.100-rc.1.26425.128
  [Host]     : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.11 (10.0.11, 10.0.1126.37416), Arm64 RyuJIT armv8.0-a


```
| Method               | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------- |---------:|---------:|---------:|------:|--------:|-------:|-------:|----------:|------------:|
| &#39;Driver: repository&#39; | 907.5 ns | 17.79 ns | 15.77 ns |  1.00 |    0.02 | 0.6590 | 0.0057 |    5512 B |        1.00 |
| &#39;MongoFlow: vault&#39;   | 130.2 ns |  2.63 ns |  2.33 ns |  0.14 |    0.00 | 0.0715 |      - |     600 B |        0.11 |
