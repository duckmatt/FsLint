module FSharpLint.Core.Tests.Rules.Formatting.TypePrefixing

open NUnit.Framework
open FSharpLint.Rules

[<TestFixture>]
type internal TestFormattingTypePrefixing() =
    inherit TestAstNodeRuleBase.TestAstNodeRuleBase(TypePrefixing.rule)

    [<Test>]
    member this.``Error for F# List type prefix syntax``() =
        this.Parse """
module Program

type T = list<int>
"""

        Assert.IsTrue(this.ErrorExistsAt(4, 9))

    [<Test>]
    member this.``No error for F# List type postfix syntax``() =
        this.Parse """
module Program

type T = int list
"""

        Assert.IsTrue(this.NoErrorsExist)

    [<Test>]
    member this.``Error for F# Option type prefix syntax``() =
        this.Parse """
module Program

type T = Option<int>
"""

        Assert.IsTrue(this.ErrorExistsAt(4, 9))

    [<Test>]
    member this.``No error for F# Option type postfix syntax``() =
        this.Parse """
module Program

type T = int option
"""

        Assert.IsTrue(this.NoErrorsExist)

    [<Test>]
    member this.``Error for F# ref type prefix syntax``() =
        this.Parse """
module Program

type T = ref<int>
"""

        Assert.IsTrue(this.ErrorExistsAt(4, 9))

    [<Test>]
    member this.``No error for F# ref type postfix syntax``() =
        this.Parse """
module Program

type T = int ref
"""

        Assert.IsTrue(this.NoErrorsExist)

    [<Test>]
    member this.``Error for F# array type prefix syntax``() =
        this.Parse """
module Program

type T = array<int>
"""

        Assert.IsTrue(this.ErrorExistsAt(4, 9))

    [<Test>]
    member this.``Error for F# array type standard postfix syntax``() =
        this.Parse """
module Program

type T = int array
"""

        Assert.IsTrue(this.ErrorExistsAt(4, 9))

    [<Test>]
    member this.``No error for F# array type special postfix syntax``() =
        this.Parse """
module Program

type T = int []
"""

        Assert.IsTrue(this.NoErrorsExist)

    [<Test>]
    member this.``Error for generic type postfix syntax``() =
        this.Parse """
module Program

type X = int Generic
"""

        Assert.IsTrue(this.ErrorExistsAt(4, 9))

    [<Test>]
    member this.``No error for generic type prefix syntax``() =
        this.Parse """
module Program

type X = Generic<int>
"""

        Assert.IsTrue(this.NoErrorsExist)

    [<Test>]
    member this.``Quickfix for F# List type``() =
        let source = """
module Program

type T = list<int>
"""

        let expected = """
module Program

type T = int list
"""

        this.Parse source
        Assert.AreEqual(expected, this.ApplyQuickFix source)

    [<Test>]
    member this.``Quickfix for F# Option type``() =
        let source = """
module Program

type T = option<int>
"""

        let expected = """
module Program

type T = int option
"""

        this.Parse source
        Assert.AreEqual(expected, this.ApplyQuickFix source)

    [<Test>]
    member this.``Quickfix for F# Ref type``() =
        let source = """
module Program

type T = ref<int>
"""

        let expected = """
module Program

type T = int ref
"""

        this.Parse source
        Assert.AreEqual(expected, this.ApplyQuickFix source)

    [<Test>]
    member this.``Quickfix for F# array type from prefix syntax``() =
        let source = """
module Program

type T = array<int>
"""

        let expected = """
module Program

type T = int []
"""

        this.Parse source
        Assert.AreEqual(expected, this.ApplyQuickFix source)

    [<Test>]
    member this.``Quickfix for F# array type from standard postfix syntax``() =
        let source = """
module Program

type T = int array
"""

        let expected = """
module Program

type T = int []
"""

        this.Parse source
        Assert.AreEqual(expected, this.ApplyQuickFix source)

    [<Test>]
    member this.``Quickfix for F# array tuple type from standard postfix syntax``() =
        let source = """
module Program

type T = (int * int) array
"""

        let expected = """
module Program

type T = (int * int) []
"""

        this.Parse source
        Assert.AreEqual(expected, this.ApplyQuickFix source)

    [<Test>]
    member this.``Quickfix for generic type``() =
        let source = """
module Program

type T = int Generic
"""

        let expected = """
module Program

type T = Generic<int>
"""

        this.Parse source
        Assert.AreEqual(expected, this.ApplyQuickFix source)

