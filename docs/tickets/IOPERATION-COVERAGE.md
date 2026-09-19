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
| CompoundAssignment | opaque: reason `CompoundAssignment` (not in the M2-003 list) | IrLowererTests.UnsupportedConstructIsOpaqueWithItsName | M2-003 |
| Conditional | n/a: `if` and `?:` become CFG branches, lowered as `br` and phis | IrLowererSnapshotTests.IfElse, .NestedIf | M2-003 |
| ConstructorBodyOperation | opaque: whole body, reason `ConstructorBodyOperation` | IrLowererTests.WholeBodyIsOneOpaque | M2-003 |
| Conversion | lowered: integral to integral (zext/sext/trunc, checked narrowing throws); anything else opaque with reason `Conversion` | IrLowererSnapshotTests.Conversions, .CheckedConversion | M2-003 |
| ExpressionStatement | lowered: its operation, value discarded | IrLowererSnapshotTests.OpaqueCall | M2-003 |
| FieldReference | opaque: reason `FieldReference`, also as an assignment target (fields are M2-004) | IrLowererSnapshotTests.FieldAndThrowAreOpaque | M2-003 |
| FlowCapture | lowered: a variable; a captured local or parameter is also an lvalue | IrLowererSnapshotTests.ConditionalExpression; IrLowererTests.CapturedLocalIsAssignedThroughTheCapture | M2-003 |
| FlowCaptureReference | lowered: a read of the capture variable, or the captured lvalue as an assignment target | IrLowererSnapshotTests.ConditionalExpression | M2-003 |
| Increment | opaque: reason `Increment` (not in the M2-003 list) | IrLowererTests.UnsupportedConstructIsOpaqueWithItsName | M2-003 |
| Invalid | opaque: erroneous code only; reason `Invalid` | IrLowererTests.FallingOffANonVoidMethodIsMissingReturn | M2-003 |
| Invocation | lowered: `IrCall` plus a threw edge to `System.Exception`; receiver not a value type: reason `dereference`; `ref`/`out` argument: reason `ref-argument` | IrLowererSnapshotTests.OpaqueCall; IrLowererTests.UnsupportedConstructIsOpaqueWithItsName | M2-003 |
| Literal | lowered: integral and bool constants; other literals (string, floating point, null) opaque with reason `Literal` | IrLowererTests.UnsignedAndCharConstantsKeepTheirBits | M2-003 |
| LocalReference | lowered: SSA variable | IrLowererSnapshotTests.StraightLineArithmetic | M2-003 |
| Loop | opaque: whole body, reason `loop` (M2-004) | IrLowererSnapshotTests.EntirelyOpaque | M2-003 |
| MethodBodyOperation | lowered: the root: its CFG is lowered | IrLowererSnapshotTests.* | M2-003 |
| ObjectCreation | opaque: reason `ObjectCreation` | IrLowererSnapshotTests.FieldAndThrowAreOpaque | M2-003 |
| ParameterReference | lowered: SSA variable; a primary-constructor parameter is opaque with reason `ParameterReference` | IrLowererSnapshotTests.RefAndOutParameters; IrLowererTests.PrimaryConstructorParameterIsOpaque | M2-003 |
| PropertyReference | opaque: reason `PropertyReference` | IrLowererTests.UnsupportedConstructIsOpaqueWithItsName | M2-003 |
| Return | lowered: a CFG Return branch becomes `ret` | IrLowererSnapshotTests.VoidEarlyReturn | M2-003 |
| SimpleAssignment | lowered: to a local, parameter or captured lvalue; other targets opaque with the target's kind; ref assignment opaque with reason `SimpleAssignment` | IrLowererSnapshotTests.StraightLineArithmetic; IrLowererTests.UnsupportedConstructIsOpaqueWithItsName | M2-003 |
| Switch | opaque: whole body, reason `switch` (M2-004) | IrLowererTests.WholeBodyIsOneOpaque | M2-003 |
| SwitchExpression | opaque: whole body, reason `switch` (M2-004) | IrLowererTests.WholeBodyIsOneOpaque | M2-003 |
| Throw | opaque: reason `Throw`: the thrown object's dynamic type is not known statically | IrLowererSnapshotTests.FieldAndThrowAreOpaque | M2-003 |
| Try | opaque: whole body, reason `try-region` (M2-004) | IrLowererTests.WholeBodyIsOneOpaque | M2-003 |
| Unary | lowered: `!`, `~`, `-` (checked: overflow edge), `+` on integral/bool; anything else opaque with reason `Unary` | IrLowererTests.CheckedNegationOverflowsOnlyAtMinValue, .BitwiseNotAndUnaryPlusLower | M2-003 |
| Using | opaque: whole body, reason `try-region` (the CFG gives it a finally region) | IrLowererTests.WholeBodyIsOneOpaque | M2-003 |
| VariableDeclaration | n/a: the CFG turns an initializer into a `SimpleAssignment` | IrLowererSnapshotTests.StraightLineArithmetic | M2-003 |
| VariableDeclarationGroup | n/a: as `VariableDeclaration` | IrLowererSnapshotTests.StraightLineArithmetic | M2-003 |
| VariableDeclarator | n/a: as `VariableDeclaration` | IrLowererSnapshotTests.StraightLineArithmetic | M2-003 |
| VariableInitializer | n/a: as `VariableDeclaration` | IrLowererSnapshotTests.StraightLineArithmetic | M2-003 |
