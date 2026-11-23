# FormuLang.NET.Abstractions

Interfaces and enumerations of [FormuLang.NET](https://github.com/Adisoreq/formulang-dotnet), the .NET implementation of the [**FormuLang**](https://github.com/Adisoreq/FormuLang) formula language. No dependencies.

- **Primitives**: `IValue`, `IType`, `IContainer`, `IObject`, `IError`.
- **Functions**: `IFunction`, `IFunctionOverload`, `IArgumentTemplate`, `ILambda`.
- **Pipeline stages**: `IValidator`, `ICleaner`, `ITokenizer`, `IParser`, `IEvaluator` and their results.
- **Structures**: `IEnvironment`, `IContext`, `IStandard`.

Most applications reference [FormuLang.NET.Core](https://www.nuget.org/packages/FormuLang.NET.Core), which implements these interfaces. Reference this package directly to replace a stage of the execution pipeline, or to write an extension standard without depending on the Core implementation.

[Architecture](https://github.com/Adisoreq/formulang-dotnet/blob/main/docs/architecture.md) · [Changelog](https://github.com/Adisoreq/formulang-dotnet/blob/main/CHANGELOG.md)
