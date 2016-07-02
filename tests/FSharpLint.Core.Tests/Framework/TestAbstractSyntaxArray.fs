﻿// FSharpLint, a linter for F#.
// Copyright (C) 2016 Matthew Mcveigh
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

module TestAbstractSyntaxArray

open System.IO
open System.Diagnostics
open FSharpLint.Framework
open FSharpLint.Framework.Ast
open FSharpLint.Framework.AbstractSyntaxArray
open Microsoft.FSharp.Compiler.Ast
open Microsoft.FSharp.Compiler.Range
open Microsoft.FSharp.Compiler.SourceCodeServices
open NUnit.Framework

[<TestFixture>]
type TestAst() =

    [<Literal>]
    let SourceFile = "../../../TypeChecker.fs"

    let generateAst source =
        let checker = FSharpChecker.Create()

        let options = 
            checker.GetProjectOptionsFromScript(SourceFile, source) 
            |> Async.RunSynchronously

        let parseResults =
            checker.ParseFileInProject(SourceFile, source, options)
            |> Async.RunSynchronously
        
        match parseResults.ParseTree with
        | Some(parseTree) -> parseTree
        | None -> failwith "Failed to parse file."

    let astToExpr ast =
        let (|Module|_|) x =
            match x with
            | SynModuleOrNamespace(_, _, _, SynModuleDecl.DoExpr(_, app, _)::_, _, _, _, _) ->
                Some(app)
            | _ -> None

        match ast with
        | ParsedInput.ImplFile(x) -> 
            match x with 
            | ParsedImplFileInput(_, _, _, _, _, Module(app)::_, _) -> 
                app 
            | _ -> failwith "Expected at least one module or namespace."
        | _ -> failwith "Expected an implementation file."

    let astNodeName = string >> (fun x -> x.Substring(x.LastIndexOf("+") + 1))

    [<Test>]
    member __.``Flatten with right pipe adds lhs to end of function application.``() = 
        match generateAst "x |> List.map (fun x -> x)" |> astToExpr |> Expression with
        | FuncApp(expressions, _) -> 
            Assert.AreEqual(["LongIdent"; "Lambda"; "Ident"], expressions |> List.map astNodeName)
        | _ -> Assert.Fail()

    [<Test>]
    member __.``Flatten with left pipe adds rhs to end of function application.``() = 
        match generateAst "List.map (fun x -> x) <| x" |> astToExpr |> Expression with
        | FuncApp(expressions, _) -> 
            Assert.AreEqual(["LongIdent"; "Lambda"; "Ident"], expressions |> List.map astNodeName)
        | _ -> Assert.Fail()

    [<Test>]
    member __.``Flatten with right pipe adds lhs to end of function application no matter the number of arguments on rhs.``() = 
        match generateAst "x |> List.map (fun x -> x) 1" |> astToExpr |> Expression with
        | FuncApp(expressions, _) -> 
            Assert.AreEqual(["LongIdent"; "Lambda"; "Const"; "Ident"], expressions |> List.map astNodeName)
        | _ -> Assert.Fail()

    [<Test>]
    member __.``Flatten with binary operator on lhs of right pipe.``() = 
        match generateAst "x::[] |> List.map (fun x -> x)" |> astToExpr |> Expression with
        | FuncApp(expressions, _) -> 
            Assert.AreEqual(["LongIdent"; "Lambda"; "App"], expressions |> List.map astNodeName)
        | _ -> Assert.Fail()

    [<Test>]
    member __.``Flatten with function application on lhs of right pipe.``() = 
        match generateAst "(foo x) |> List.map (fun x -> x)" |> astToExpr |> Expression with
        | FuncApp(expressions, _) -> 
            Assert.AreEqual(["LongIdent"; "Lambda"; "App"], expressions |> List.map astNodeName)
        | _ -> Assert.Fail()

    [<Test>]
    member __.``Flatten with multiple right pipes.``() = 
        match generateAst "x |> foo |> List.map (fun x -> x)" |> astToExpr |> Expression with
        | FuncApp(expressions, _) -> 
            Assert.AreEqual(["LongIdent"; "Lambda"; "App"], expressions |> List.map astNodeName)
        | _ -> Assert.Fail()

    [<Test>]
    member __.``Flatten with multiple left pipes.``() = 
        match generateAst "List.map (fun x -> x) <| 1 <| x" |> astToExpr |> Expression with
        | FuncApp(expressions, _) -> 
            Assert.AreEqual(["LongIdent"; "Lambda"; "Const"; "Ident"], expressions |> List.map astNodeName)
        | _ -> Assert.Fail()

    [<Category("Performance")>]
    [<Test>]
    member __.``Performance of building syntax array``() = 
        let tree = File.ReadAllText SourceFile |> generateAst

        let iterations = 100

        let stopwatch = Stopwatch.StartNew()
        let times = ResizeArray()

        for _ in 0..iterations do
            stopwatch.Restart()
            
            astToArray tree |> ignore

            stopwatch.Stop()

            times.Add stopwatch.ElapsedMilliseconds

        let result = times |> Seq.sum |> (fun totalMilliseconds -> totalMilliseconds / int64 iterations)

        Assert.Less(result, 200)
        System.Console.WriteLine(sprintf "Built array in an average of %d milliseconds." result)

    [<Test>]
    member __.``Syntax array constructed from AST in valid order.``() = 
        let tree = generateAst "List.map (fun x y -> id x) woofs"

        let (array, skipArray) = astToArray tree

        let actual = array |> Array.map (fun x -> x.Hashcode)

        let expected =
            [ Utilities.hash2 SyntaxNode.ModuleOrNamespace 0
              Utilities.hash2 SyntaxNode.ModuleDeclaration 0
              Utilities.hash2 SyntaxNode.FuncApp 0
              Utilities.hash2 SyntaxNode.Identifier "map"
              Utilities.hash2 SyntaxNode.Lambda 0
              Utilities.hash2 SyntaxNode.LambdaArg 0
              Utilities.hash2 SyntaxNode.Identifier "x"
              Utilities.hash2 SyntaxNode.LambdaArg 0
              Utilities.hash2 SyntaxNode.Identifier "y"
              Utilities.hash2 SyntaxNode.LambdaBody 0
              Utilities.hash2 SyntaxNode.FuncApp 0
              Utilities.hash2 SyntaxNode.Identifier "id"
              Utilities.hash2 SyntaxNode.Identifier "x"
              Utilities.hash2 SyntaxNode.Identifier "woofs" ]

        Assert.AreEqual(expected, actual)
        Assert.AreEqual([ AbstractSyntaxArray.Skip(13, 0)
                          AbstractSyntaxArray.Skip(12, 0)
                          AbstractSyntaxArray.Skip(11, 1)
                          AbstractSyntaxArray.Skip(0, 2)
                          AbstractSyntaxArray.Skip(8, 2)
                          AbstractSyntaxArray.Skip(1, 4)
                          AbstractSyntaxArray.Skip(0, 5)
                          AbstractSyntaxArray.Skip(1, 4)
                          AbstractSyntaxArray.Skip(0, 7)
                          AbstractSyntaxArray.Skip(3, 4)
                          AbstractSyntaxArray.Skip(2, 9)
                          AbstractSyntaxArray.Skip(0, 10)
                          AbstractSyntaxArray.Skip(0, 10)
                          AbstractSyntaxArray.Skip(0, 2) ], skipArray)

    /// e.g. a lambda arg shouldn't have the body of the lambda in its child nodes (that should be a sibling).
    [<Test>]
    member __.``Syntax array's extra info nodes do not contain children of node they're generated from.``() = 
        let tree = generateAst "fun x -> x"

        let (array, skipArray) = astToArray tree

        let actual = array |> Array.map (fun x -> x.Hashcode)

        let expected =
            [ Utilities.hash2 SyntaxNode.ModuleOrNamespace 0
              Utilities.hash2 SyntaxNode.ModuleDeclaration 0
              Utilities.hash2 SyntaxNode.Lambda 0
              Utilities.hash2 SyntaxNode.LambdaArg 0
              Utilities.hash2 SyntaxNode.Identifier "x"
              Utilities.hash2 SyntaxNode.LambdaBody 0
              Utilities.hash2 SyntaxNode.Identifier "x" ]

        Assert.AreEqual(expected, actual)
        Assert.AreEqual([ AbstractSyntaxArray.Skip(6, 0)
                          AbstractSyntaxArray.Skip(5, 0)
                          AbstractSyntaxArray.Skip(4, 1)
                          AbstractSyntaxArray.Skip(1, 2)
                          AbstractSyntaxArray.Skip(0, 3)
                          AbstractSyntaxArray.Skip(1, 2)
                          AbstractSyntaxArray.Skip(0, 5) ], skipArray)