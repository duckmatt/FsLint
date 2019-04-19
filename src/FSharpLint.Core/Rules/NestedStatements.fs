﻿namespace FSharpLint.Rules

module NestedStatements =

    open System
    open FSharp.Compiler.Ast
    open FSharpLint.Framework
    open FSharpLint.Framework.Analyser
    open FSharpLint.Framework.Ast
    open FSharpLint.Framework.Configuration

    [<Literal>]
    let AnalyserName = "NestedStatements"

    let private configDepth config =
        match isAnalyserEnabled config AnalyserName with
        | Some(analyser) ->
            match Map.tryFind "Depth" analyser.Settings with
            | Some(Depth(l)) -> Some(l)
            | Some(_) | None -> None
        | Some(_) | None -> None

    let private error (depth:int) =
        let errorFormatString = Resources.GetString("RulesNestedStatementsError")
        String.Format(errorFormatString, depth)

    /// Lambda wildcard arguments are named internally as _argN, a match is then generated for them in the AST.
    /// e.g. fun _ -> () is represented in the AST as fun _arg1 -> match _arg1 with | _ -> ().
    /// This function returns true if the given match statement is compiler generated for a lmabda wildcard argument.
    let private isCompilerGeneratedMatch = function
        | SynExpr.Match(_, SynExpr.Ident(ident), _, _) when ident.idText.StartsWith("_arg") -> true
        | _ -> false

    let private areChildrenNested = function
        | AstNode.Binding(SynBinding.Binding(_))
        | AstNode.Expression(SynExpr.Lambda(_))
        | AstNode.Expression(SynExpr.MatchLambda(_))
        | AstNode.Expression(SynExpr.IfThenElse(_))
        | AstNode.Expression(SynExpr.Lazy(_))
        | AstNode.Expression(SynExpr.Record(_))
        | AstNode.Expression(SynExpr.ObjExpr(_))
        | AstNode.Expression(SynExpr.TryFinally(_))
        | AstNode.Expression(SynExpr.TryWith(_))
        | AstNode.Expression(SynExpr.Tuple(_))
        | AstNode.Expression(SynExpr.Quote(_))
        | AstNode.Expression(SynExpr.While(_))
        | AstNode.Expression(SynExpr.For(_))
        | AstNode.Expression(SynExpr.ForEach(_)) -> true
        | AstNode.Expression(SynExpr.Match(_) as matchExpr) when not (isCompilerGeneratedMatch matchExpr) -> true
        | _ -> false

    let private getRange = function
        | AstNode.Expression(node) -> Some node.Range
        | AstNode.Binding(node) -> Some node.RangeOfBindingAndRhs
        | _ -> None

    let private distanceToCommonParent (syntaxArray:AbstractSyntaxArray.Node []) (skipArray:AbstractSyntaxArray.Skip []) i j =
        let mutable i = i
        let mutable j = j
        let mutable distance = 0

        while i <> j do
            if i > j then
                i <- skipArray.[i].ParentIndex

                if i <> j && areChildrenNested syntaxArray.[i].Actual then
                    distance <- distance + 1
            else
                j <- skipArray.[j].ParentIndex

        distance
        
    let analyser (args: AnalyserArgs) : unit =
        let syntaxArray, skipArray = args.SyntaxArray, args.SkipArray

        let isSuppressed i =
            AbstractSyntaxArray.getSuppressMessageAttributes syntaxArray skipArray i
            |> AbstractSyntaxArray.isRuleSuppressed AnalyserName AbstractSyntaxArray.SuppressRuleWildcard

        match configDepth args.Info.Config with
        | Some(errorDepth) ->
            let error = error errorDepth

            /// Is node a duplicate of a node in the AST containing ExtraSyntaxInfo
            /// e.g. lambda arg being a duplicate of the lamdba.
            let isMetaData node i =
                let parentIndex = skipArray.[i].ParentIndex
                if parentIndex = i then false
                else
                    Object.ReferenceEquals(node, syntaxArray.[parentIndex].Actual)

            let isElseIf node i =
                match node with
                | AstNode.Expression(SynExpr.IfThenElse(_)) ->
                    let parentIndex = skipArray.[i].ParentIndex
                    if parentIndex = i then false
                    else
                        match syntaxArray.[parentIndex].Actual with
                        | AstNode.Expression(SynExpr.IfThenElse(_, _, Some(_), _, _, _, _)) -> true
                        | _ -> false
                | _ -> false

            let mutable depth = 0

            let decrementDepthToCommonParent i j =
                if j < syntaxArray.Length then
                    // If next node in array is not a sibling or child of the current node.
                    let parent = skipArray.[j].ParentIndex
                    if parent <> i && parent <> skipArray.[i].ParentIndex then
                        // Decrement depth until we reach a common parent.
                        depth <- depth - (distanceToCommonParent syntaxArray skipArray i j)

            let mutable i = 0
            while i < syntaxArray.Length do
                decrementDepthToCommonParent i (i + 1)

                let node = syntaxArray.[i].Actual

                if areChildrenNested node && not <| isMetaData node i && not <| isElseIf node i then
                    if depth >= errorDepth then
                        if not (isSuppressed i) then
                            getRange node
                            |> Option.iter (fun range ->
                                args.Info.Suggest
                                    { Range = range; Message = error; SuggestedFix = None; TypeChecks = [] })

                        // Skip children as we've had an error containing them.
                        let skipChildren = i + skipArray.[i].NumberOfChildren + 1
                        decrementDepthToCommonParent i skipChildren
                        i <- skipChildren
                    else
                        depth <- depth + 1
                        i <- i + 1
                else
                    i <- i + 1
        | None -> ()
