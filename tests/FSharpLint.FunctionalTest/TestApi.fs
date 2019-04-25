﻿namespace FSharpLint.FunctionalTest

module TestApi =

    open System.IO
    open System.Diagnostics
    open NUnit.Framework
    open FSharpLint.Application.Lint
    open FSharp.Compiler.SourceCodeServices

    let (</>) x y = Path.Combine(x, y)

    let basePath = TestContext.CurrentContext.TestDirectory </> ".." </> ".." </> ".." </> ".." </> ".."

    let sourceFile = basePath </> "tests" </> "TypeChecker.fs"
    
    [<TestFixture(Category = "Acceptance Tests")>]
    type TestApi() =
        let generateAst source =
            let checker = FSharpChecker.Create()

            let (options, _diagnostics) = 
                checker.GetProjectOptionsFromScript(sourceFile, source) 
                |> Async.RunSynchronously

            let parseResults =
                checker.ParseFile(sourceFile, source, options |> checker.GetParsingOptionsFromProjectOptions |> fst)
                |> Async.RunSynchronously
        
            match parseResults.ParseTree with
            | Some(parseTree) -> parseTree
            | None -> failwith "Failed to parse file."

        [<Category("Performance")>]
        [<Test>]
        member __.``Performance of linting an existing file``() = 
            let text = File.ReadAllText sourceFile
            let tree = text |> generateAst
            let fileInfo = { Ast = tree; Source = text; TypeCheckResults = None }
            
            let stopwatch = Stopwatch.StartNew()
            let times = ResizeArray()

            let iterations = 100

            for _ in 0..iterations do
                stopwatch.Restart()
                
                let result = lintParsedFile OptionalLintParameters.Default fileInfo sourceFile

                stopwatch.Stop()

                times.Add stopwatch.ElapsedMilliseconds

            let result = times |> Seq.sum |> (fun totalMilliseconds -> totalMilliseconds / int64 iterations)
            
            Assert.Less(result, 250)
            System.Console.WriteLine(sprintf "Average runtime of linter on parsed file: %d (milliseconds)."  result)
            
            
        [<Category("Performance")>]
        [<Test>]
        member __.``test``() =
            let text = """
type IExampleInterface =
   abstract member Print : unit -> unit

[<EntryPoint>]
let main argv =
    let x = List.fold (+) 0 [1;2;3]
    printfn "%d" x
    0
"""
            let tree = text |> generateAst
            let fileInfo = { Ast = tree; Source = text; TypeCheckResults = None }
            

            let result = lintParsedFile OptionalLintParameters.Default fileInfo sourceFile

            
            match result with
            | LintResult.Success warnings ->
                printfn "%O" warnings
            | _ -> ()
                
