﻿namespace FSharpLint.Application

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open System.Threading
open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices
open Dotnet.ProjInfo.Inspect.MSBuild
open FSharpLint.Core
open FSharpLint.Framework
open FSharpLint.Framework.Configuration
open FSharpLint.Framework.Rules
open FSharpLint.Rules

/// Provides an API to manage/load FSharpLint configuration files.
/// <see cref="FSharpLint.Framework.Configuration" /> for more information on
/// the default configuration.
module ConfigurationManagement =

    /// Load a FSharpLint configuration file given its contents.
    let loadConfigurationFile (configText:string) =
        parseConfig configText

/// Provides an API for running FSharpLint from within another application.
[<AutoOpen>]
module Lint =

    type BuildFailure = InvalidProjectFileMessage of string

    /// Reason for the linter failing.
    [<NoComparison>]
    type LintFailure =
        /// Project file path did not exist on the local filesystem.
        | ProjectFileCouldNotBeFound of string

        /// Received exception when trying to get the list of F# file from the project file.
        | MSBuildFailedToLoadProjectFile of string * BuildFailure

        /// Failed to load a FSharpLint configuration file.
        | FailedToLoadConfig of string

        /// The specified file for linting could not be found.
        | FailedToLoadFile of string

        /// Failed to analyse a loaded FSharpLint configuration at runtime e.g. invalid hint.
        | RunTimeConfigError of string

        /// `FSharp.Compiler.Services` failed when trying to parse a file.
        | FailedToParseFile of ParseFile.ParseFileFailure

        /// `FSharp.Compiler.Services` failed when trying to parse one or more files in a project.
        | FailedToParseFilesInProject of ParseFile.ParseFileFailure list

        member this.Description
            with get() =
                let getParseFailureReason = function
                    | ParseFile.FailedToParseFile failures ->
                        let getFailureReason (x:FSharp.Compiler.SourceCodeServices.FSharpErrorInfo) =
                            sprintf "failed to parse file %s, message: %s" x.FileName x.Message

                        String.Join(", ", failures |> Array.map getFailureReason)
                    | ParseFile.AbortedTypeCheck -> "Aborted type check."

                match this with
                | ProjectFileCouldNotBeFound projectPath ->
                    String.Format(Resources.GetString("ConsoleProjectFileCouldNotBeFound"), projectPath)
                | MSBuildFailedToLoadProjectFile (projectPath, InvalidProjectFileMessage(message)) ->
                    String.Format(Resources.GetString("ConsoleMSBuildFailedToLoadProjectFile"), projectPath, message)
                | FailedToLoadFile filepath ->
                    String.Format(Resources.GetString("ConsoleCouldNotFindFile"), filepath)
                | FailedToLoadConfig message ->
                    String.Format(Resources.GetString("ConsoleFailedToLoadConfig"), message)
                | RunTimeConfigError error ->
                    String.Format(Resources.GetString("ConsoleRunTimeConfigError"), error)
                | FailedToParseFile failure ->
                    "Lint failed to parse a file. Failed with: " + getParseFailureReason failure
                | FailedToParseFilesInProject failures ->
                    let failureReasons = String.Join("\n", failures |> List.map getParseFailureReason)
                    "Lint failed to parse files. Failed with: " + failureReasons

    [<NoComparison>]
    type Result<'t> =
        | Success of 't
        | Failure of LintFailure

    /// Provides information on what the linter is currently doing.
    [<NoComparison>]
    type ProjectProgress =
        /// Started parsing a file (file path).
        | Starting of string

        /// Finished parsing a file (file path).
        | ReachedEnd of string * Suggestion.LintWarning list

        /// Failed to parse a file (file path, exception that caused failure).
        | Failed of string * System.Exception
    with
        /// Path of the F# file the progress information is for.
        member this.FilePath() =
            match this with
            | Starting(f)
            | ReachedEnd(f, _)
            | Failed(f, _) -> f

    [<NoEquality; NoComparison>]
    type LintInfo =
        { CancellationToken: CancellationToken option
          ErrorReceived: Suggestion.LintWarning -> unit
          ReportLinterProgress: ProjectProgress -> unit
          Configuration: Configuration.Configuration }

    type Context =
        { indentationRuleContext : Map<int,bool*int>
          noTabCharactersRuleContext : (string * Range.range) list }

    let runAstNodeRules (rules:RuleMetadata<AstNodeRuleConfig> []) typeCheckResults (filePath:string) (fileContent:string) syntaxArray skipArray =
        let mutable indentationRuleState = Map.empty
        let mutable noTabCharactersRuleState = List.empty

        // Collect suggestions for AstNode rules, and build context for following rules.
        let astNodeSuggestions =
            syntaxArray
            |> Array.mapi (fun i astNode -> (i, astNode))
            |> Array.collect (fun (i, astNode) ->
                let getParents (depth:int) = AbstractSyntaxArray.getBreadcrumbs depth syntaxArray skipArray i
                let astNodeParams =
                    { astNode = astNode.Actual
                      nodeHashcode = astNode.Hashcode
                      nodeIndex =  i
                      syntaxArray = syntaxArray
                      skipArray = skipArray
                      getParents = getParents
                      filePath = filePath
                      fileContent = fileContent
                      checkInfo = typeCheckResults }
                // Build state for rules with context.
                indentationRuleState <- Indentation.ContextBuilder.builder indentationRuleState astNode.Actual
                noTabCharactersRuleState <- NoTabCharacters.ContextBuilder.builder noTabCharactersRuleState astNode.Actual

                rules
                |> Array.collect (fun rule -> runAstNodeRule rule astNodeParams))

        let context =
            { indentationRuleContext = indentationRuleState
              noTabCharactersRuleContext = noTabCharactersRuleState }

        rules |> Array.iter (fun rule -> rule.ruleConfig.cleanup())
        (astNodeSuggestions, context)

    let runLineRules (lineRules:Configuration.LineRules) (filePath:string) (fileContent:string) (context:Context) =
        fileContent
        |> String.toLines
        |> Array.collect (fun (line, lineNumber, isLastLine) ->
            let lineParams =
                { LineRuleParams.line = line
                  lineNumber = lineNumber + 1
                  isLastLine = isLastLine
                  filePath = filePath
                  fileContent = fileContent }

            let indentationError =
                lineRules.indentationRule
                |> Option.map (fun rule -> runLineRuleWithContext rule context.indentationRuleContext lineParams)

            let noTabCharactersError =
                lineRules.noTabCharactersRule
                |> Option.map (fun rule -> runLineRuleWithContext rule context.noTabCharactersRuleContext lineParams)

            let lineErrors =
                lineRules.genericLineRules
                |> Array.collect (fun rule -> runLineRule rule lineParams)

            [|
                indentationError |> Option.toArray
                noTabCharactersError |> Option.toArray
                lineErrors |> Array.singleton
            |])
        |> Array.concat
        |> Array.concat

    let isSuppressed (suppressedRulesByLine:IDictionary<int, Set<string>>) (suggestion:Suggestion.LintWarning) =
        // Filter out any suggestion which has one of its lines suppressed.
        [suggestion.Details.Range.StartLine..suggestion.Details.Range.EndLine]
        |> List.exists (fun lineNum ->
            match suppressedRulesByLine.TryGetValue(lineNum - 1) with
            | (true, suppressedRules) -> Set.contains suggestion.RuleName suppressedRules
            | (false, _) -> false)

    let lint lintInfo (fileInfo:ParseFile.FileParseInfo) =
        let suggestionsRequiringTypeChecks = ConcurrentStack<_>()

        let fileWarnings = ResizeArray()

        let suggest (warning:Suggestion.LintWarning) =
            lintInfo.ErrorReceived warning
            fileWarnings.Add warning

        let trySuggest (suggestion:Suggestion.LintWarning) =
            if suggestion.Details.TypeChecks.IsEmpty then suggest suggestion
            else suggestionsRequiringTypeChecks.Push suggestion

        Starting(fileInfo.file) |> lintInfo.ReportLinterProgress

        let cancelHasNotBeenRequested () =
            match lintInfo.CancellationToken with
            | Some(x) -> not x.IsCancellationRequested
            | None -> true

        let enabledRules = Configuration.flattenConfig lintInfo.Configuration

        let lines = String.toLines fileInfo.text |> Array.map (fun (line, _, _) -> line) |> Array.toList
        let allRuleNames =
            [|
                enabledRules.lineRules.indentationRule |> Option.map (fun rule -> rule.name) |> Option.toArray
                enabledRules.lineRules.noTabCharactersRule |> Option.map (fun rule -> rule.name) |> Option.toArray
                enabledRules.lineRules.genericLineRules |> Array.map (fun rule -> rule.name)
                enabledRules.astNodeRules |> Array.map (fun rule -> rule.name)
            |] |> Array.concat |> Set.ofArray
        let suppressedRulesByLine = Suppression.getSuppressedRulesPerLine allRuleNames lines

        try
            let (syntaxArray, skipArray) = AbstractSyntaxArray.astToArray fileInfo.ast

            // Collect suggestions for AstNode rules
            let (astNodeSuggestions, context) = runAstNodeRules enabledRules.astNodeRules fileInfo.typeCheckResults fileInfo.file fileInfo.text syntaxArray skipArray
            let lineSuggestions = runLineRules enabledRules.lineRules fileInfo.file fileInfo.text context

            [|
                lineSuggestions
                astNodeSuggestions
            |]
            |> Array.concat
            |> Array.filter (isSuppressed suppressedRulesByLine >> not)
            |> Array.iter trySuggest

            if cancelHasNotBeenRequested () then
                let runSynchronously work =
                    let timeoutMs = 2000
                    match lintInfo.CancellationToken with
                    | Some(cancellationToken) -> Async.RunSynchronously(work, timeoutMs, cancellationToken)
                    | None -> Async.RunSynchronously(work, timeoutMs)

                try
                    let typeChecksSuccessful (typeChecks: Async<bool> list) =
                        typeChecks
                        |> List.reduce (Async.combine (&&))

                    let typeCheckSuggestion (suggestion: Suggestion.LintWarning) =
                        typeChecksSuccessful suggestion.Details.TypeChecks
                        |> Async.map (fun checkSuccessful -> if checkSuccessful then Some suggestion else None)

                    suggestionsRequiringTypeChecks
                    |> Seq.map typeCheckSuggestion
                    |> Async.Parallel
                    |> runSynchronously
                    |> Array.iter (function Some(suggestion) -> suggest suggestion | None -> ())
                with
                | :? TimeoutException -> () // Do nothing.
        with
        | e -> Failed(fileInfo.file, e) |> lintInfo.ReportLinterProgress

        ReachedEnd(fileInfo.file, fileWarnings |> Seq.toList) |> lintInfo.ReportLinterProgress

    let private runProcess (workingDir:string) (exePath:string) (args:string) =
        let psi = System.Diagnostics.ProcessStartInfo()
        psi.FileName <- exePath
        psi.WorkingDirectory <- workingDir
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.Arguments <- args
        psi.CreateNoWindow <- true
        psi.UseShellExecute <- false

        use p = new System.Diagnostics.Process()
        p.StartInfo <- psi
        let sbOut = System.Text.StringBuilder()
        p.OutputDataReceived.Add(fun ea -> sbOut.AppendLine(ea.Data) |> ignore)
        let sbErr = System.Text.StringBuilder()
        p.ErrorDataReceived.Add(fun ea -> sbErr.AppendLine(ea.Data) |> ignore)
        p.Start() |> ignore
        p.BeginOutputReadLine()
        p.BeginErrorReadLine()
        p.WaitForExit()

        let exitCode = p.ExitCode

        (exitCode, (workingDir, exePath, args))

    let getProjectFileInfo (releaseConfig : string option) (projectFilePath: string) =
        let projDir = System.IO.Path.GetDirectoryName projectFilePath

        let msBuildParams =
            releaseConfig
            |> Option.map (fun config -> [MSbuildCli.Property("ConfigurationName", config)])
            |> Option.defaultValue []

        let msBuildResults =
            let runCmd exePath args = runProcess projDir exePath (args |> String.concat " ")
            let msbuildExec = Dotnet.ProjInfo.Inspect.dotnetMsbuild runCmd

            projectFilePath
            |> Dotnet.ProjInfo.Inspect.getProjectInfos ignore msbuildExec [Dotnet.ProjInfo.Inspect.getFscArgs; Dotnet.ProjInfo.Inspect.getResolvedP2PRefs] msBuildParams

        match msBuildResults with
        | Result.Ok [getFscArgsResult] ->
            match getFscArgsResult with
            | Result.Ok (Dotnet.ProjInfo.Inspect.GetResult.FscArgs fa) ->

                let projDir = Path.GetDirectoryName projectFilePath

                let isSourceFile (option:string) =
                    option.TrimStart().StartsWith("-") |> not

                let compileFilesToAbsolutePath (f: string) =
                    if Path.IsPathRooted f then
                        f
                    else
                        Path.Combine(projDir, f)

                { ProjectFileName = projectFilePath
                  SourceFiles = fa |> List.filter isSourceFile |> List.map compileFilesToAbsolutePath |> Array.ofList
                  OtherOptions = fa |> List.filter (isSourceFile >> not) |> Array.ofList
                  ReferencedProjects = [||]
                  IsIncompleteTypeCheckEnvironment = false
                  UseScriptResolutionRules = false
                  LoadTime = DateTime.Now
                  UnresolvedReferences = None
                  OriginalLoadReferences = []
                  ExtraProjectInfo = None
                  ProjectId = None
                  Stamp = None }
            | Result.Ok _ ->
                failwithf "error getting FSC args from msbuild info"
            | Result.Error error ->
                failwithf "error getting FSC args from msbuild info, %A" error
        | Result.Ok r ->
            failwithf "error getting msbuild info: internal error, more info returned than expected %A" r
        | Result.Error r ->
            failwithf "error getting msbuild info: internal error, more info returned than expected %A" r

    let getFailedFiles = function
        | ParseFile.Failed failure -> Some failure
        | _ -> None

    let getParsedFiles = function
        | ParseFile.Success file -> Some file
        | _ -> None

    /// Result of running the linter.
    [<RequireQualifiedAccess; NoEquality; NoComparison>]
    type LintResult =
        | Success of Suggestion.LintWarning list
        | Failure of LintFailure

        member self.TryGetSuccess([<Out>] success:byref<Suggestion.LintWarning list>) =
            match self with
            | Success value -> success <- value; true
            | _ -> false
        member self.TryGetFailure([<Out>] failure:byref<LintFailure>) =
            match self with
            | Failure value -> failure <- value; true
            | _ -> false

    type ConfigurationParam =
        | Configuration of Configuration
        | FromFile of configPath:string
        | Default

    /// Optional parameters that can be provided to the linter.
    [<NoEquality; NoComparison>]
    type OptionalLintParameters = {
        /// Cancels a lint in progress.
        CancellationToken: CancellationToken option
        /// Lint configuration to use.
        /// Can either specify a full configuration object, or a path to a file to load the configuration from.
        /// You can also explicitly specify the default configuration.
        Configuration: ConfigurationParam
        /// This function will be called every time the linter finds a broken rule.
        ReceivedWarning: (Suggestion.LintWarning -> unit) option
        /// This function will be called any time the linter progress changes for a project.
        ReportLinterProgress: (ProjectProgress -> unit) option
        /// The configuration under which the linter will try to perform parsing.
        ReleaseConfiguration : string option
    } with
        static member Default = {
            OptionalLintParameters.CancellationToken = None
            Configuration = Default
            ReceivedWarning = None
            ReportLinterProgress = None
            ReleaseConfiguration = None
        }

    /// If your application has already parsed the F# source files using `FSharp.Compiler.Services`
    /// you want to lint then this can be used to provide the parsed information to prevent the
    /// linter from parsing the file again.
    [<NoEquality; NoComparison>]
    type ParsedFileInformation = {
        /// File represented as an AST.
        Ast: FSharp.Compiler.Ast.ParsedInput
        /// Contents of the file.
        Source: string
        /// Optional results of inferring the types on the AST (allows for a more accurate lint).
        TypeCheckResults: FSharpCheckFileResults option
    }

    /// Gets a FSharpLint Configuration based on the provided ConfigurationParam.
    let private getConfig (configParam:ConfigurationParam) =
        match configParam with
        | Configuration config -> Ok config
        | FromFile filePath ->
            try
                Configuration.loadConfig filePath
                |> Ok
            with
            | ex -> Error (string ex)
        | Default -> Ok Configuration.defaultConfiguration

    /// Lints an entire F# project by retrieving the files from a given
    /// path to the `.fsproj` file.
    let lintProject (optionalParams:OptionalLintParameters) (projectFilePath:string) =
        if IO.File.Exists projectFilePath then
            let projectFilePath = Path.GetFullPath projectFilePath
            let lintWarnings = LinkedList<Suggestion.LintWarning>()

            match getConfig optionalParams.Configuration with
            | Ok config ->
                let projectProgress = Option.defaultValue ignore optionalParams.ReportLinterProgress

                let warningReceived (warning:Suggestion.LintWarning) =
                    lintWarnings.AddLast warning |> ignore

                    optionalParams.ReceivedWarning |> Option.iter (fun func -> func warning)

                let checker = FSharpChecker.Create()

                let parseFilesInProject files projectOptions =
                    let lintInformation =
                        { Configuration = config
                          CancellationToken = optionalParams.CancellationToken
                          ErrorReceived = warningReceived
                          ReportLinterProgress = projectProgress }

                    let isIgnoredFile filePath =
                        config.ignoreFiles
                        |> Option.map (fun ignoreFiles ->
                            let parsedIgnoreFiles = ignoreFiles |> Array.map IgnoreFiles.parseIgnorePath |> Array.toList
                            Configuration.IgnoreFiles.shouldFileBeIgnored parsedIgnoreFiles filePath)
                        |> Option.defaultValue false

                    let parsedFiles =
                        files
                        |> List.filter (not << isIgnoredFile)
                        |> List.map (fun file -> ParseFile.parseFile file checker (Some projectOptions))

                    let failedFiles = parsedFiles |> List.choose getFailedFiles

                    if List.isEmpty failedFiles then
                        parsedFiles
                        |> List.choose getParsedFiles
                        |> List.iter (lint lintInformation)

                        Success ()
                    else
                        Failure (FailedToParseFilesInProject failedFiles)

                let projectOptions = getProjectFileInfo optionalParams.ReleaseConfiguration projectFilePath
                let compileFiles = projectOptions.SourceFiles |> Array.toList
                match parseFilesInProject compileFiles projectOptions with
                | Success _ -> lintWarnings |> Seq.toList |> LintResult.Success
                | Failure x -> LintResult.Failure x
            | Error err ->
                RunTimeConfigError err
                |> LintResult.Failure
        else
            FailedToLoadFile projectFilePath
            |> LintResult.Failure

    /// Lints an entire F# solution by linting all projects specified in the `.sln` file.
    let lintSolution (optionalParams:OptionalLintParameters) (solutionFilePath:string) =
        if IO.File.Exists solutionFilePath then
            let solutionFilePath = Path.GetFullPath solutionFilePath
            let solutionFolder = Path.GetDirectoryName solutionFilePath

            // Pre-load configuration so it isn't reloaded for every project.
            match getConfig optionalParams.Configuration with
            | Ok config ->
                let optionalParams = { optionalParams with Configuration = ConfigurationParam.Configuration config }

                let projectsInSolution =
                    File.ReadAllText(solutionFilePath)
                    |> String.toLines
                    |> Array.filter (fun (s, _, _) ->  s.StartsWith("Project") && s.Contains(".fsproj"))
                    |> Array.map (fun (s, _, _) ->
                        let endIndex = s.IndexOf(".fsproj") + 7
                        let startIndex = s.IndexOf(",") + 1
                        let projectPath = s.Substring(startIndex, endIndex - startIndex)
                        projectPath.Trim([|'"'; ' '|]))
                    |> Array.map (fun projectPath ->
                        let projectPath =
                            if Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
                                projectPath
                            else // For non-Windows, we need to convert the project path in the solution to Unix format.
                                projectPath.Replace("\\", "/")
                        Path.Combine(solutionFolder, projectPath))

                let (successes, failures) =
                    projectsInSolution
                    |> Array.map (fun projectFilePath -> lintProject optionalParams projectFilePath)
                    |> Array.fold (fun (successes, failures) result ->
                        match result with
                        | LintResult.Success warnings ->
                            (List.append warnings successes, failures)
                        | LintResult.Failure err ->
                            (successes, err :: failures)) ([], [])

                match failures with
                | [] ->
                    LintResult.Success successes
                | firstErr :: _ ->
                    LintResult.Failure firstErr

            | Error err -> LintResult.Failure (RunTimeConfigError err)
        else
            FailedToLoadFile solutionFilePath
            |> LintResult.Failure

    /// Lints F# source code that has already been parsed using `FSharp.Compiler.Services` in the calling application.
    let lintParsedSource optionalParams parsedFileInfo =
        match getConfig optionalParams.Configuration with
        | Ok config ->
            let lintWarnings = LinkedList<Suggestion.LintWarning>()

            let warningReceived (warning:Suggestion.LintWarning) =
                lintWarnings.AddLast warning |> ignore

                optionalParams.ReceivedWarning |> Option.iter (fun func -> func warning)
            let lintInformation =
                { Configuration = config
                  CancellationToken = optionalParams.CancellationToken
                  ErrorReceived = warningReceived
                  ReportLinterProgress = Option.defaultValue ignore optionalParams.ReportLinterProgress }

            let parsedFileInfo =
                { ParseFile.text = parsedFileInfo.Source
                  ParseFile.ast = parsedFileInfo.Ast
                  ParseFile.typeCheckResults = parsedFileInfo.TypeCheckResults
                  ParseFile.file = "/home/user/Dog.Test.fsx" }

            lint lintInformation parsedFileInfo

            lintWarnings |> Seq.toList |> LintResult.Success
        | Error err ->
            LintResult.Failure (RunTimeConfigError err)

    /// Lints F# source code.
    let lintSource optionalParams source =
        let checker = FSharpChecker.Create()

        match ParseFile.parseSource source checker with
        | ParseFile.Success(parseFileInformation) ->
            let parsedFileInfo =
                { Source = parseFileInformation.text
                  Ast = parseFileInformation.ast
                  TypeCheckResults = parseFileInformation.typeCheckResults }

            lintParsedSource optionalParams parsedFileInfo
        | ParseFile.Failed failure -> LintResult.Failure(FailedToParseFile failure)

    /// Lints an F# file that has already been parsed using `FSharp.Compiler.Services` in the calling application.
    let lintParsedFile (optionalParams:OptionalLintParameters) (parsedFileInfo:ParsedFileInformation) (filePath:string) =
        match getConfig optionalParams.Configuration with
        | Ok config ->
            let lintWarnings = LinkedList<Suggestion.LintWarning>()

            let warningReceived (warning:Suggestion.LintWarning) =
                lintWarnings.AddLast warning |> ignore

                optionalParams.ReceivedWarning |> Option.iter (fun func -> func warning)

            let lintInformation =
                { Configuration = config
                  CancellationToken = optionalParams.CancellationToken
                  ErrorReceived = warningReceived
                  ReportLinterProgress = Option.defaultValue ignore optionalParams.ReportLinterProgress }

            let parsedFileInfo =
                { ParseFile.text = parsedFileInfo.Source
                  ParseFile.ast = parsedFileInfo.Ast
                  ParseFile.typeCheckResults = parsedFileInfo.TypeCheckResults
                  ParseFile.file = filePath }

            lint lintInformation parsedFileInfo

            lintWarnings |> Seq.toList |> LintResult.Success
        | Error err -> LintResult.Failure (RunTimeConfigError err)

    /// Lints an F# file from a given path to the `.fs` file.
    let lintFile optionalParams filePath =
        if IO.File.Exists filePath then
            let checker = FSharpChecker.Create()

            match ParseFile.parseFile filePath checker None with
            | ParseFile.Success astFileParseInfo ->
                let parsedFileInfo =
                    { Source = astFileParseInfo.text
                      Ast = astFileParseInfo.ast
                      TypeCheckResults = astFileParseInfo.typeCheckResults }

                lintParsedFile optionalParams parsedFileInfo filePath
            | ParseFile.Failed failure -> LintResult.Failure(FailedToParseFile failure)
        else
            FailedToLoadFile filePath
            |> LintResult.Failure
