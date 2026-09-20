# IOperation lowering coverage

Maintained by the Frontend.CSharp tickets. One row per `OperationKind`. Status:
`lowered` (with test name), `opaque` (falls back to `IrOpaque`), `n/a` (never appears
in a CFG body). Every kind found in `samples/` must have a row
(`SampleLoweringTests.EveryOperationKindInTheSamplesHasACoverageRow`).

| OperationKind | Status | Test | Ticket |
|---|---|---|---|
| Argument | lowered: inside `Invocation`: its value, passed in parameter order | IrLowererTests.NamedArgumentsArePassedInParameterOrder | M2-003 |
| ArrayElementReference | opaque: reason `ArrayElementReference` (arrays are M2-004) | IrLowererTests.UnsupportedConstructIsOpaqueWithItsName | M2-003 |
| Binary | lowered: integral and bool operands; overflow, divide-by-zero and `MinValue / -1` edges; anything else opaque with reason `Binary` | IrLowererSnapshotTests.StraightLineArithmetic, .Division, .Shifts | M2-003 |
| Block | n/a: the CFG flattens blocks; an arrow-bodied accessor body is one whole-body opaque with reason `Block` | IrLowererTests.WholeBodyIsOneOpaque | M2-003 |
| CompoundAssignment | lowered: integral local or parameter target: read, promote, operate (same exception edges as the binary operator), narrow back, write; any other target opaque with the target's kind, a non-integral one with reason `CompoundAssignment` | IrLowererSnapshotTests.CompoundAssignment; IrLowererTests.CompoundAssignmentReadsOperatesAndWrites, .CompoundAssignmentToAnUnsupportedTargetIsOpaque | M2-004 |
| Conditional | n/a: `if` and `?:` become CFG branches, lowered as `br` and phis | IrLowererSnapshotTests.IfElse, .NestedIf | M2-003 |
| ConstantPattern | lowered: inside `IsPattern` | IrLowererTests.AConstantPatternOutsideASwitchIsAnEquality | M2-004 |
| ConstructorBodyOperation | opaque: whole body, reason `ConstructorBodyOperation` | IrLowererTests.WholeBodyIsOneOpaque | M2-003 |
| Conversion | lowered: integral to integral (zext/sext/trunc, checked narrowing throws); anything else opaque with reason `Conversion` | IrLowererSnapshotTests.Conversions, .CheckedConversion | M2-003 |
| Decrement | lowered: as `Increment` | IrLowererSnapshotTests.IncrementAndDecrement | M2-004 |
| DiscardPattern | lowered: inside `IsPattern`: `true` | IrLowererSnapshotTests.SwitchExpression | M2-004 |
| ExpressionStatement | lowered: its operation, value discarded | IrLowererSnapshotTests.OpaqueCall | M2-003 |
| FieldReference | opaque: reason `FieldReference`, also as an assignment target (fields are M2-004) | IrLowererSnapshotTests.FieldAndThrowAreOpaque | M2-003 |
| FlowCapture | lowered: a variable; a captured local or parameter is also an lvalue | IrLowererSnapshotTests.ConditionalExpression; IrLowererTests.CapturedLocalIsAssignedThroughTheCapture | M2-003 |
| FlowCaptureReference | lowered: a read of the capture variable, or the captured lvalue as an assignment target | IrLowererSnapshotTests.ConditionalExpression | M2-003 |
| ForEachLoop | opaque: whole body, reason `foreach-enumerator`: the CFG desugars every `foreach`, arrays included, into the enumerator pattern (ticket P1-003) | IrLowererSnapshotTests.EntirelyOpaque; IrLowererTests.WholeBodyIsOneOpaque | M2-004 |
| Increment | lowered: as `CompoundAssignment` with a promoted `1`; postfix yields the value read | IrLowererSnapshotTests.IncrementAndDecrement; IrLowererTests.IncrementAndDecrementYieldTheOldValueOnlyWhenPostfix | M2-004 |
| Invalid | opaque: erroneous code only; reason `Invalid` | IrLowererTests.FallingOffANonVoidMethodIsMissingReturn | M2-003 |
| Invocation | lowered: `IrCall` plus a threw edge to `System.Exception`; receiver not a value type: reason `dereference`; `ref`/`out` argument: reason `ref-argument` | IrLowererSnapshotTests.OpaqueCall; IrLowererTests.UnsupportedConstructIsOpaqueWithItsName | M2-003 |
| IsPattern | lowered: a constant pattern of the scrutinee's own bitvector or Bool type is an equality, a discard is `true`; every other pattern is opaque with reason `switch-pattern` | IrLowererTests.AConstantPatternOutsideASwitchIsAnEquality, .APatternBeyondAConstantIsOpaque | M2-004 |
| Literal | lowered: integral and bool constants; other literals (string, floating point, null) opaque with reason `Literal` | IrLowererTests.UnsignedAndCharConstantsKeepTheirBits | M2-003 |
| LocalReference | lowered: SSA variable | IrLowererSnapshotTests.StraightLineArithmetic | M2-003 |
| Loop | lowered: the CFG has no loop constructs, only back edges, which the SSA builder handles; a `foreach` is the exception (see `ForEachLoop`) | IrLowererSnapshotTests.WhileLoop, .ForLoop, .DoWhileLoop; IrLowererTests.LoopsLowerWithoutOpaqueNodes | M2-004 |
| MethodBodyOperation | lowered: the root: its CFG is lowered | IrLowererSnapshotTests.* | M2-003 |
| ObjectCreation | lowered: an `IrCall` to the constructor yielding the new object as a `Sort`, plus a threw edge; a `ref`/`out` argument: reason `ref-argument` | IrLowererTests.ObjectCreationIsACallToTheConstructor, .UnsupportedConstructIsOpaqueWithItsName | M2-004 |
| ParameterReference | lowered: SSA variable; a primary-constructor parameter is opaque with reason `ParameterReference` | IrLowererSnapshotTests.RefAndOutParameters; IrLowererTests.PrimaryConstructorParameterIsOpaque | M2-003 |
| PropertyReference | opaque: reason `PropertyReference` | IrLowererTests.UnsupportedConstructIsOpaqueWithItsName | M2-003 |
| Return | lowered: a CFG Return branch becomes `ret` | IrLowererSnapshotTests.VoidEarlyReturn | M2-003 |
| SimpleAssignment | lowered: to a local, parameter or captured lvalue; other targets opaque with the target's kind; ref assignment opaque with reason `SimpleAssignment` | IrLowererSnapshotTests.StraightLineArithmetic; IrLowererTests.UnsupportedConstructIsOpaqueWithItsName | M2-003 |
| Switch | n/a: the CFG turns a `switch` statement into a chain of equality branches on one captured scrutinee, which `SwitchChains` folds back into `IrSwitch` | IrLowererSnapshotTests.SwitchStatement; IrLowererTests.ASwitchOnAnIntegralScrutineeLowersToOneSwitchTerminator | M2-004 |
| SwitchExpression | n/a: the CFG turns it into a chain of `IsPattern` branches, folded as `Switch` is | IrLowererSnapshotTests.SwitchExpression | M2-004 |
| Throw | lowered: `throw new T(...)` records the constructor call then `IrThrow("T")` on T's static type; throwing anything else is opaque with reason `Throw`, and `throw;` with reason `rethrow` | IrLowererSnapshotTests.ThrowOfANewObject; IrLowererTests.ThrowOfANewObjectRecordsTheConstructorCallAndThrowsItsStaticType, .RethrowIsOpaque | M2-004 |
| Try | opaque: whole body, reason `try-region` (M2-004) | IrLowererTests.WholeBodyIsOneOpaque | M2-003 |
| Unary | lowered: `!`, `~`, `-` (checked: overflow edge), `+` on integral/bool; anything else opaque with reason `Unary` | IrLowererTests.CheckedNegationOverflowsOnlyAtMinValue, .BitwiseNotAndUnaryPlusLower | M2-003 |
| Using | opaque: whole body, reason `try-region` (the CFG gives it a finally region) | IrLowererTests.WholeBodyIsOneOpaque | M2-003 |
| VariableDeclaration | n/a: the CFG turns an initializer into a `SimpleAssignment` | IrLowererSnapshotTests.StraightLineArithmetic | M2-003 |
| VariableDeclarationGroup | n/a: as `VariableDeclaration` | IrLowererSnapshotTests.StraightLineArithmetic | M2-003 |
| VariableDeclarator | n/a: as `VariableDeclaration` | IrLowererSnapshotTests.StraightLineArithmetic | M2-003 |
| VariableInitializer | n/a: as `VariableDeclaration` | IrLowererSnapshotTests.StraightLineArithmetic | M2-003 |
