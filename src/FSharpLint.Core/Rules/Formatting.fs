﻿namespace FSharpLint.Rules

module Formatting =

    open System
    open FSharp.Compiler.Ast
    open FSharp.Compiler.Range
    open FSharpLint.Framework
    open FSharpLint.Framework.Analyser
    open FSharpLint.Framework.Ast
    open FSharpLint.Framework.Configuration
    open FSharpLint.Framework.ExpressionUtilities

    [<Literal>]
    let AnalyserName = "Formatting"

    let private isRuleEnabled config ruleName =
        isRuleEnabled config AnalyserName ruleName |> Option.isSome

    module private PatternMatchFormatting =

        let getLeadingSpaces (args : AnalyserArgs) (range : range) =
            let range = mkRange "" (mkPos range.StartLine 0) range.End
            args.Info.TryFindTextOfRange(range)
            |> Option.map (fun text ->
                text.ToCharArray()
                |> Array.takeWhile Char.IsWhiteSpace
                |> Array.length)
            |> Option.defaultValue 0

        let checkPatternMatchClausesOnNewLine args (clauses:SynMatchClause list) isSuppressed =
            let ruleName = "PatternMatchClausesOnNewLine"

            let isEnabled = isRuleEnabled args.Info.Config ruleName

            if isEnabled && isSuppressed ruleName |> not then
                clauses
                |> List.pairwise
                |> List.iter (fun (clauseOne, clauseTwo) ->
                    if clauseOne.Range.EndLine = clauseTwo.Range.StartLine then
                        args.Info.Suggest
                            { Range = clauseTwo.Range
                              Message = Resources.GetString("RulesFormattingPatternMatchClausesOnNewLineError")
                              SuggestedFix = None
                              TypeChecks = [] })

        let checkPatternMatchOrClausesOnNewLine args (clauses:SynMatchClause list) isSuppressed =
            let ruleName = "PatternMatchOrClausesOnNewLine"

            let isEnabled = isRuleEnabled args.Info.Config ruleName

            if isEnabled && isSuppressed ruleName |> not then
                clauses
                |> List.collect (function
                    | SynMatchClause.Clause (SynPat.Or (firstPat, secondPat, _), _, _, _, _) ->
                        [firstPat; secondPat]
                    | _ -> [])
                |> List.pairwise
                |> List.iter (fun (clauseOne, clauseTwo) ->
                    if clauseOne.Range.EndLine = clauseTwo.Range.StartLine then
                        args.Info.Suggest
                            { Range = clauseTwo.Range
                              Message = Resources.GetString("RulesFormattingPatternMatchOrClausesOnNewLineError")
                              SuggestedFix = None
                              TypeChecks = [] })

        let checkPatternMatchClauseIndentation args matchExprRange (clauses:SynMatchClause list) isLambda isSuppressed =
            let ruleName = "PatternMatchClauseIndentation"

            let isEnabled = isRuleEnabled args.Info.Config ruleName

            if isEnabled && isSuppressed ruleName |> not then
                let matchStartIndentation = getLeadingSpaces args matchExprRange
                clauses
                |> List.tryHead
                |> Option.iter (fun firstClause ->
                    let clauseIndentation = getLeadingSpaces args firstClause.Range
                    if isLambda then
                        if clauseIndentation <> matchStartIndentation + 4 then
                          args.Info.Suggest
                            { Range = firstClause.Range
                              Message = Resources.GetString("RulesFormattingLambdaPatternMatchClauseIndentationError")
                              SuggestedFix = None
                              TypeChecks = [] }
                    elif clauseIndentation <> matchStartIndentation then
                          args.Info.Suggest
                            { Range = firstClause.Range
                              Message = Resources.GetString("RulesFormattingPatternMatchClauseIndentationError")
                              SuggestedFix = None
                              TypeChecks = [] })

                clauses
                |> List.map (fun clause -> (clause, getLeadingSpaces args clause.Range))
                |> List.pairwise
                |> List.iter (fun ((clauseOne, clauseOneSpaces), (clauseTwo, clauseTwoSpaces)) ->
                    if clauseOneSpaces <> clauseTwoSpaces then
                        args.Info.Suggest
                            { Range = clauseTwo.Range
                              Message = Resources.GetString("RulesFormattingPatternMatchClauseSameIndentationError")
                              SuggestedFix = None
                              TypeChecks = [] })

        let checkPatternMatchExpressionIndentation args (clauses:SynMatchClause list) isSuppressed =
            let ruleName = "PatternMatchExpressionIndentation"

            let isEnabled = isRuleEnabled args.Info.Config ruleName

            if isEnabled && isSuppressed ruleName |> not then
                clauses
                |> List.iter (fun clause ->
                    let (SynMatchClause.Clause (pat, guard, expr, _, _)) = clause
                    let clauseIndentation = getLeadingSpaces args clause.Range
                    let exprIndentation = getLeadingSpaces args expr.Range
                    let matchPatternEndLine =
                        guard
                        |> Option.map (fun expr -> expr.Range.EndLine)
                        |> Option.defaultValue pat.Range.EndLine
                    if expr.Range.StartLine <> matchPatternEndLine
                    && exprIndentation <> clauseIndentation + 4 then
                      args.Info.Suggest
                        { Range = expr.Range
                          Message = Resources.GetString("RulesFormattingMatchExpressionIndentationError")
                          SuggestedFix = None
                          TypeChecks = [] })

    module private TypePrefixing =

        let checkTypePrefixing args range typeName typeArgs isPostfix isSuppressed =
            let ruleName = "TypePrefixing"

            let isEnabled = isRuleEnabled args.Info.Config ruleName

            if isEnabled && isSuppressed ruleName |> not then
                match typeName with
                | SynType.LongIdent lid ->
                    match lid |> longIdentWithDotsToString with
                    | "list"
                    | "List"
                    | "option"
                    | "Option"
                    | "ref"
                    | "Ref" as typeName ->
                        // Prefer postfix.
                        if not isPostfix
                        then
                            let errorFormatString = Resources.GetString("RulesFormattingF#PostfixGenericError")
                            let suggestedFix = lazy(
                                (args.Info.TryFindTextOfRange range, typeArgs)
                                ||> Option.map2 (fun fromText typeArgs -> { FromText = fromText; FromRange = range; ToText = typeArgs + " " + typeName }))
                            args.Info.Suggest
                                { Range = range
                                  Message =  String.Format(errorFormatString, typeName)
                                  SuggestedFix = Some suggestedFix
                                  TypeChecks = [] }
                    | "array" ->
                        // Prefer special postfix (e.g. int []).
                        let suggestedFix = lazy(
                            (args.Info.TryFindTextOfRange range, typeArgs)
                            ||> Option.map2 (fun fromText typeArgs -> { FromText = fromText; FromRange = range; ToText = typeArgs + " []" }))
                        args.Info.Suggest
                            { Range = range
                              Message = Resources.GetString("RulesFormattingF#ArrayPostfixError")
                              SuggestedFix = Some suggestedFix
                              TypeChecks = [] }
                    | typeName ->
                        // Prefer prefix.
                        if isPostfix
                        then
                            let suggestedFix = lazy(
                                (args.Info.TryFindTextOfRange range, typeArgs)
                                ||> Option.map2 (fun fromText typeArgs -> { FromText = fromText; FromRange = range; ToText = typeName + "<" + typeArgs + ">" }))
                            args.Info.Suggest
                                { Range = range
                                  Message = Resources.GetString("RulesFormattingGenericPrefixError")
                                  SuggestedFix = Some suggestedFix
                                  TypeChecks = [] }
                | _ -> ()

    module private Spacing =

        let countPrecedingCommentLines (args : AnalyserArgs) (startPos : pos) (endPos : pos) =
            mkRange
                ""
                startPos
                endPos
            |> args.Info.TryFindTextOfRange
            |> Option.map (fun preceedingText ->
                let lines =
                    preceedingText.Split '\n'
                    |> Array.rev
                    |> Array.tail
                lines
                |> Array.takeWhile (fun line -> line.TrimStart().StartsWith("//"))
                |> Array.length)
            |> Option.defaultValue 0

        let checkModuleDeclSpacing args synModuleOrNamespace isSuppressed =
            let ruleName = "ModuleDeclSpacing"

            let isEnabled = isRuleEnabled args.Info.Config ruleName

            if isEnabled && isSuppressed ruleName |> not then

                match synModuleOrNamespace with
                | SynModuleOrNamespace (_, _, _, decls, _, _, _, _) ->
                    decls
                    |> List.pairwise
                    |> List.iter (fun (declOne, declTwo) ->
                        let numPreceedingCommentLines = countPrecedingCommentLines args declOne.Range.End declTwo.Range.Start
                        if declTwo.Range.StartLine <> declOne.Range.EndLine + 3 + numPreceedingCommentLines then
                            let intermediateRange =
                                let startLine = declOne.Range.EndLine + 1
                                let endLine = declTwo.Range.StartLine
                                let endOffset =
                                    if startLine = endLine
                                    then 1
                                    else 0

                                mkRange
                                    ""
                                    (mkPos (declOne.Range.EndLine + 1) 0)
                                    (mkPos (declTwo.Range.StartLine + endOffset) 0)
                            args.Info.Suggest
                                { Range = intermediateRange
                                  Message = Resources.GetString("RulesFormattingModuleDeclSpacingError")
                                  SuggestedFix = None
                                  TypeChecks = [] })

        let checkClassMemberSpacing args (members:SynMemberDefns) isSuppressed =
            let ruleName = "ClassMemberSpacing"

            let isEnabled = isRuleEnabled args.Info.Config ruleName

            if isEnabled && isSuppressed ruleName |> not then
                members
                |> List.pairwise
                |> List.iter (fun (memberOne, memberTwo) ->
                    let numPreceedingCommentLines = countPrecedingCommentLines args memberOne.Range.End memberTwo.Range.Start
                    if memberTwo.Range.StartLine <> memberOne.Range.EndLine + 2 + numPreceedingCommentLines then
                        let intermediateRange =
                            let startLine = memberOne.Range.EndLine + 1
                            let endLine = memberTwo.Range.StartLine
                            let endOffset =
                                if startLine = endLine
                                then 1
                                else 0

                            mkRange
                                ""
                                (mkPos (memberOne.Range.EndLine + 1) 0)
                                (mkPos (memberTwo.Range.StartLine + endOffset) 0)
                        args.Info.Suggest
                            { Range = intermediateRange
                              Message = Resources.GetString("RulesFormattingClassMemberSpacingError")
                              SuggestedFix = None
                              TypeChecks = [] })

    module private TypeDefinitionFormatting =

        let getUnionCaseStartColumn (args : AnalyserArgs) (SynUnionCase.UnionCase (attrs, _, _, _, _, range)) =
            match attrs |> List.tryHead with
            | Some attr ->
                mkRange "" (mkPos attr.Range.StartLine 0) attr.Range.Start
                |> args.Info.TryFindTextOfRange
                |> Option.bind (fun preceedingText ->
                    let attrStartIndex = preceedingText.IndexOf "[<"
                    if attrStartIndex = -1
                    then None
                    else Some attrStartIndex)
                |> Option.defaultValue (attr.Range.StartColumn - 2)
            | None ->
                range.StartColumn

        let checkUnionDefinitionIndentation args typeDefnRepr typeDefnStartColumn isSuppressed =
            let ruleName = "UnionDefinitionIndentation"

            let isEnabled = isRuleEnabled args.Info.Config ruleName

            if isEnabled && isSuppressed ruleName |> not then

                match typeDefnRepr with
                | SynTypeDefnRepr.Simple((SynTypeDefnSimpleRepr.Union (_, cases, _)), _) ->
                    match cases with
                    | []
                    | [_] -> ()
                    | firstCase :: _ ->
                        if getUnionCaseStartColumn args firstCase <> typeDefnStartColumn + 1 then
                          args.Info.Suggest
                            { Range = firstCase.Range
                              Message = Resources.GetString("RulesFormattingUnionDefinitionIndentationError")
                              SuggestedFix = None
                              TypeChecks = [] }

                        cases
                        |> List.pairwise
                        |> List.iter (fun (caseOne, caseTwo) ->
                            if getUnionCaseStartColumn args caseOne <> getUnionCaseStartColumn args caseTwo then
                                args.Info.Suggest
                                    { Range = caseTwo.Range
                                      Message = Resources.GetString("RulesFormattingUnionDefinitionSameIndentationError")
                                      SuggestedFix = None
                                      TypeChecks = [] })
                | _ -> ()

    let analyser (args: AnalyserArgs) : unit =
        let syntaxArray, skipArray = args.SyntaxArray, args.SkipArray

        let isSuppressed i ruleName =
            AbstractSyntaxArray.getSuppressMessageAttributes syntaxArray skipArray i
            |> AbstractSyntaxArray.isRuleSuppressed AnalyserName ruleName

        let synTypeToString = function
            | SynType.Tuple _ as synType ->
                args.Info.TryFindTextOfRange synType.Range
                |> Option.map (fun x -> "(" + x + ")")
            | other ->
                args.Info.TryFindTextOfRange other.Range

        let typeArgsToString (typeArgs:SynType list) =
            let typeStrings = typeArgs |> List.choose synTypeToString
            if typeStrings.Length = typeArgs.Length
            then typeStrings |> String.concat "," |> Some
            else None

        for i = 0 to syntaxArray.Length - 1 do
            match syntaxArray.[i].Actual with
            | AstNode.Expression (SynExpr.Match (_, _, clauses, range))
            | AstNode.Expression (SynExpr.MatchLambda (_, _, clauses, _, range))
            | AstNode.Expression (SynExpr.TryWith (_, _, clauses, range, _, _, _)) as node ->
                let isLambda =
                    match node with
                    | AstNode.Expression (SynExpr.MatchLambda _) -> true
                    | _ -> false

                let isFunctionParameter =
                    AbstractSyntaxArray.getBreadcrumbs 3 syntaxArray skipArray i
                    |> List.exists (function
                        | Expression (SynExpr.Lambda _ ) -> true
                        | _ -> false)

                // Ignore pattern matching in function parameters.
                if not (isFunctionParameter) then
                    PatternMatchFormatting.checkPatternMatchClausesOnNewLine args clauses (isSuppressed i)
                    PatternMatchFormatting.checkPatternMatchOrClausesOnNewLine args clauses (isSuppressed i)
                    PatternMatchFormatting.checkPatternMatchClauseIndentation args range clauses isLambda (isSuppressed i)
                    PatternMatchFormatting.checkPatternMatchExpressionIndentation args clauses (isSuppressed i)
            | AstNode.Type (SynType.App (typeName, _, typeArgs, _, _, isPostfix, range)) ->
                let typeArgs = typeArgsToString typeArgs
                TypePrefixing.checkTypePrefixing args range typeName typeArgs isPostfix (isSuppressed i)
            | AstNode.ModuleOrNamespace synModuleOrNamespace ->
                Spacing.checkModuleDeclSpacing args synModuleOrNamespace (isSuppressed i)
            | AstNode.TypeDefinition (SynTypeDefn.TypeDefn (_, repr, members, defnRange)) ->
                Spacing.checkClassMemberSpacing args members (isSuppressed i)
                TypeDefinitionFormatting.checkUnionDefinitionIndentation args repr defnRange.StartColumn (isSuppressed i)
            | AstNode.TypeRepresentation (SynTypeDefnRepr.ObjectModel (_, members, _)) ->
                Spacing.checkClassMemberSpacing args members (isSuppressed i)
            | _ -> ()
