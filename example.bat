cd LogicalOptimizer
dotnet run -- "a & a & a | a & a | !a & 0 | a & 1 | (a | a) & a | a & (b | !b) | a & (c & !c) | !(!(a)) | a | 0 | 1 & a | a & a & a & a"
dotnet run -- "((a & !b & !c) | (!a & b & !c) | (!a & !b & c) | (a & b & c)) & ((a & !b & !c) | (!a & b & !c) | (!a & !b & c) | (a & b & c)) & 1 | 0 & x | ((a & !b & !c) | (!a & b & !c) | (!a & !b & c) | (a & b & c)) | ((a & !b & !c) | (!a & b & !c) | (!a & !b & c) | (a & b & c))"
dotnet run -- "((a & !b) | (!a & b)) & ((a & !b) | (!a & b)) & 1 | (!x | y) & (!x | y) & (!x | y) | 0 & z | ((a & !b) | (!a & b)) | (!x | y)"
cd ..