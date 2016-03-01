﻿(*
    FSharpLint, a linter for F#.
    Copyright (C) 2014 Matthew Mcveigh

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*)

namespace FSharpLint.Framework

module AstTemp =

    open Microsoft.FSharp.Compiler.Ast
    open FSharpLint.Framework.Ast

    /// Extracts an expression from parentheses e.g. ((x + 4)) -> x + 4
    let rec removeParens = function
        | SynExpr.Paren(x, _, _, _) -> removeParens x
        | x -> x
        
    let flattenFunctionApplication expr =
        let rec flatten exprs = function
            | SynExpr.App(_, _, x, y, _) -> 
                match removeParens x with
                | SynExpr.App(_, true, SynExpr.Ident(op), rightExpr, _) as infixApp ->
                    match op.idText with
                    | "op_PipeRight" | "op_PipeRight2" | "op_PipeRight3" ->
                        let flattened = flatten [] y
                        flattened@[rightExpr]
                    | "op_PipeLeft" | "op_PipeLeft2" | "op_PipeLeft3" ->
                        let flattened = flatten [] rightExpr
                        flattened@[y]
                    | _ -> [infixApp]
                | x -> flatten (removeParens y::exprs) x
            | x -> x::exprs

        flatten [] expr

    let flattenLambda lambda =
        let (|IsCurriedLambda|_|) = function
            | SynExpr.Lambda(_, _, parameter, (SynExpr.Lambda(_) as inner), _) as outer when outer.Range = inner.Range ->
                Some(parameter, inner)
            | _ -> None

        let rec getLambdaParametersAndExpression parameters = function
            | IsCurriedLambda(parameter, curriedLambda) ->
                getLambdaParametersAndExpression (parameter::parameters) curriedLambda
            | SynExpr.Lambda(_, _, parameter, body, _) ->
                (parameter::parameters |> List.rev, body)
            | body -> 
                assert false
                (parameters |> List.rev, body)

        getLambdaParametersAndExpression [] lambda
        
    let inline private longIdentToString (longIdent:LongIdent) =
        longIdent |> List.map (fun x -> x.idText) |> String.concat "."

    let inline private longIdentWithDotsToString (LongIdentWithDots(longIdent, _)) =
        longIdentToString longIdent

    let inline private moduleDeclarationChildren node = 
        match node with
        | SynModuleDecl.NestedModule(componentInfo, moduleDeclarations, _, _) -> 
            ComponentInfo componentInfo::(moduleDeclarations |> List.map ModuleDeclaration)
        | SynModuleDecl.Let(_, bindings, _) -> bindings |> List.map Binding
        | SynModuleDecl.DoExpr(_, expression, _) -> [Expression expression]
        | SynModuleDecl.Types(typeDefinitions, _) -> typeDefinitions |> List.map TypeDefinition
        | SynModuleDecl.Exception(exceptionDefinition, _) -> [ExceptionDefinition exceptionDefinition]
        | SynModuleDecl.NamespaceFragment(moduleOrNamespace) -> [ModuleOrNamespace moduleOrNamespace]
        | SynModuleDecl.Open(_)
        | SynModuleDecl.Attributes(_)
        | SynModuleDecl.HashDirective(_)
        | SynModuleDecl.ModuleAbbrev(_) -> []

    let inline private typeChildren node =
        match node with
        | SynType.LongIdentApp(synType, _, _, types, _, _, _)
        | SynType.App(synType, _, types, _, _, _, _) -> 
            Type synType::(types |> List.map Type)
        | SynType.Tuple(types, _) -> 
            types |> List.map (snd >> Type)
        | SynType.Fun(synType, synType1, _)
        | SynType.StaticConstantNamed(synType, synType1, _)
        | SynType.MeasureDivide(synType, synType1, _) -> 
            [Type synType; Type synType1]
        | SynType.Var(_)
        | SynType.Anon(_)
        | SynType.LongIdent(_)
        | SynType.StaticConstant(_) -> []
        | SynType.WithGlobalConstraints(synType, _, _)
        | SynType.HashConstraint(synType, _)
        | SynType.MeasurePower(synType, _, _)
        | SynType.Array(_, synType, _) -> [Type synType]
        | SynType.StaticConstantExpr(expression, _) -> [Expression expression]

    let inline private memberDefinitionChildren node = 
        match node with
        | SynMemberDefn.Member(binding, _) -> [Binding binding]
        | SynMemberDefn.ImplicitCtor(_, _, patterns, _, _) -> patterns |> List.map SimplePattern
        | SynMemberDefn.ImplicitInherit(synType, expression, _, _) -> 
            [Type synType; Expression expression]
        | SynMemberDefn.LetBindings(bindings, _, _, _) -> bindings |> List.map Binding
        | SynMemberDefn.Interface(synType, Some(members), _) -> 
            Type synType::(members |> List.map MemberDefinition)
        | SynMemberDefn.Interface(synType, None, _)
        | SynMemberDefn.Inherit(synType, _, _) -> [Type synType]
        | SynMemberDefn.Open(_)
        | SynMemberDefn.AbstractSlot(_) -> []
        | SynMemberDefn.ValField(field, _) -> [Field field]
        | SynMemberDefn.NestedType(typeDefinition, _, _) -> [TypeDefinition typeDefinition]
        | SynMemberDefn.AutoProperty(_, _, _, Some(synType), _, _, _, _, expression, _, _) -> 
            [Type synType; Expression expression]
        | SynMemberDefn.AutoProperty(_, _, _, None, _, _, _, _, expression, _, _) -> [Expression expression]

    let inline private patternChildren node =
        match node with 
        | SynPat.IsInst(synType, _) -> [Type synType]
        | SynPat.QuoteExpr(expression, _) -> [Expression expression]
        | SynPat.Typed(pattern, synType, _) -> [Pattern pattern; Type synType]
        | SynPat.Or(pattern, pattern1, _) -> [Pattern pattern; Pattern pattern1]
        | SynPat.ArrayOrList(_, patterns, _)
        | SynPat.Tuple(patterns, _)
        | SynPat.Ands(patterns, _) -> patterns |> List.map Pattern
        | SynPat.LongIdent(_, _, _, constructorArguments, _, _) -> [ConstructorArguments constructorArguments]
        | SynPat.Attrib(pattern, _, _)
        | SynPat.Named(pattern, _, _, _, _)
        | SynPat.Paren(pattern, _) -> [Pattern pattern]
        | SynPat.Record(patternsAndIdentifier, _) -> patternsAndIdentifier |> List.map (snd >> Pattern)
        | SynPat.Const(_)
        | SynPat.Wild(_)
        | SynPat.FromParseError(_)
        | SynPat.InstanceMember(_)
        | SynPat.DeprecatedCharRange(_)
        | SynPat.Null(_)
        | SynPat.OptionalVal(_) -> []

    let inline private expressionChildren node =
        match node with 
        | SynExpr.Paren(expression, _, _, _)
        | SynExpr.DotGet(expression, _, _, _)
        | SynExpr.DotIndexedGet(expression, _, _, _)
        | SynExpr.LongIdentSet(_, expression, _)
        | SynExpr.Do(expression, _)
        | SynExpr.Assert(expression, _)
        | SynExpr.CompExpr(_, _, expression, _)
        | SynExpr.ArrayOrListOfSeqExpr(_, expression, _)
        | SynExpr.AddressOf(_, expression, _, _)
        | SynExpr.InferredDowncast(expression, _)
        | SynExpr.InferredUpcast(expression, _)
        | SynExpr.DoBang(expression, _)
        | SynExpr.Lazy(expression, _)
        | SynExpr.TraitCall(_, _, expression, _)
        | SynExpr.YieldOrReturn(_, expression, _)
        | SynExpr.YieldOrReturnFrom(_, expression, _) -> [Expression expression]
        | SynExpr.Quote(expression, _, expression1, _, _)
        | SynExpr.Sequential(_, _, expression, expression1, _)
        | SynExpr.NamedIndexedPropertySet(_, expression, expression1, _)
        | SynExpr.DotIndexedSet(expression, _, expression1, _, _, _)
        | SynExpr.JoinIn(expression, _, expression1, _)
        | SynExpr.While(_, expression, expression1, _)
        | SynExpr.TryFinally(expression, expression1, _, _, _)
        | SynExpr.DotSet(expression, _, expression1, _) -> 
            [Expression expression; Expression expression1]
        | SynExpr.Typed(expression, synType, _) -> 
            [Expression expression; Type synType]
        | SynExpr.Tuple(expressions, _, _)
        | SynExpr.ArrayOrList(_, expressions, _) -> expressions |> List.map Expression
        | SynExpr.Record(_, Some(expr, _), _, _) -> [Expression expr]
        | SynExpr.Record(_, None, _, _) -> []
        | SynExpr.ObjExpr(synType, _, bindings, _, _, _) -> 
            Type synType::(bindings |> List.map Binding)
        | SynExpr.ImplicitZero(_)
        | SynExpr.Null(_)
        | SynExpr.Const(_)
        | SynExpr.DiscardAfterMissingQualificationAfterDot(_)
        | SynExpr.FromParseError(_)
        | SynExpr.LibraryOnlyILAssembly(_)
        | SynExpr.LibraryOnlyStaticOptimization(_)
        | SynExpr.LibraryOnlyUnionCaseFieldGet(_)
        | SynExpr.LibraryOnlyUnionCaseFieldSet(_)
        | SynExpr.ArbitraryAfterError(_) -> []
        | SynExpr.DotNamedIndexedPropertySet(expression, _, expression1, expression2, _)
        | SynExpr.For(_, _, expression, _, expression1, expression2, _) -> 
            [Expression expression; Expression expression1; Expression expression2]
        | SynExpr.LetOrUseBang(_, _, _, pattern, expression, expression1, _)
        | SynExpr.ForEach(_, _, _, pattern, expression, expression1, _) -> 
            [Pattern pattern; Expression expression; Expression expression1]
        | SynExpr.MatchLambda(_, _, matchClauses, _, _) -> 
            matchClauses |> List.map Match
        | SynExpr.TryWith(expression, _, matchClauses, _, _, _, _)
        | SynExpr.Match(_, expression, matchClauses, _, _) -> 
            Expression expression::(matchClauses |> List.map Match)
        | SynExpr.TypeApp(expression, _, types, _, _, _, _) -> 
            Expression expression::(types |> List.map Type)
        | SynExpr.New(_, synType, expression, _) 
        | SynExpr.TypeTest(expression, synType, _)
        | SynExpr.Upcast(expression, synType, _)
        | SynExpr.Downcast(expression, synType, _) -> 
            [Expression expression; Type synType]
        | SynExpr.LetOrUse(_, _, bindings, expression, _) -> 
            [ yield! bindings |> List.map Binding
              yield Expression expression ]
        | SynExpr.IfThenElse(cond, body, None, _, _, _, _) -> [Expression(cond);Expression(body)]
        | SynExpr.IfThenElse(cond, body, Some(elseExpr), _, _, _, _) -> [Expression(cond);Expression(body);Expression(elseExpr)]
              
        | SynExpr.Ident(ident) -> [Identifier(ident.idText)]
        | SynExpr.LongIdent(_, ident, _, _) -> [Identifier(longIdentWithDotsToString ident)]
        | SynExpr.Lambda(_) as lambda -> 
            let (args, body) = flattenLambda lambda
            [Lambda(args |> List.map LambdaArg.LambdaArg, LambdaBody.LambdaBody(body))]
        | SynExpr.App(_) as app -> [FuncApp(flattenFunctionApplication app)]

    let inline private typeSimpleRepresentationChildren node =
        match node with 
        | SynTypeDefnSimpleRepr.Union(_, unionCases, _) -> unionCases |> List.map UnionCase
        | SynTypeDefnSimpleRepr.Enum(enumCases, _) -> enumCases |> List.map EnumCase
        | SynTypeDefnSimpleRepr.Record(_, fields, _) -> fields |> List.map Field
        | SynTypeDefnSimpleRepr.TypeAbbrev(_, synType, _) -> [Type synType]
        | SynTypeDefnSimpleRepr.General(_)
        | SynTypeDefnSimpleRepr.LibraryOnlyILAssembly(_)
        | SynTypeDefnSimpleRepr.None(_) -> []

    let inline private simplePatternsChildren node =
        match node with 
        | SynSimplePats.SimplePats(simplePatterns, _) -> simplePatterns |> List.map SimplePattern
        | SynSimplePats.Typed(simplePatterns, synType, _) -> 
            [SimplePatterns simplePatterns; Type synType]

    let inline private simplePatternChildren node =
        match node with 
        | SynSimplePat.Typed(simplePattern, synType, _) -> 
            [SimplePattern simplePattern; Type synType]
        | SynSimplePat.Attrib(simplePattern, _, _) -> [SimplePattern simplePattern]
        | SynSimplePat.Id(identifier, _, _, _, _, _) -> [Identifier(identifier.idText)]

    let inline private matchChildren node =
        match node with 
        | Clause(pattern, Some(expression), expression1, _, _) -> 
            [Pattern pattern; Expression expression; Expression expression1]
        | Clause(pattern, None, expression1, _, _) -> 
            [Pattern pattern; Expression expression1]

    let inline private constructorArgumentsChildren node =
        match node with 
        | SynConstructorArgs.Pats(patterns) -> patterns |> List.map Pattern
        | SynConstructorArgs.NamePatPairs(namePatterns, _) -> namePatterns |> List.map (snd >> Pattern)

    let inline private typeRepresentationChildren node =
        match node with 
        | SynTypeDefnRepr.ObjectModel(_, members, _) -> members |> List.map MemberDefinition
        | SynTypeDefnRepr.Simple(typeSimpleRepresentation, _) -> [TypeSimpleRepresentation typeSimpleRepresentation]

    let traverseNode node =
        match node with
        | ModuleDeclaration(x) -> moduleDeclarationChildren x
        | ModuleOrNamespace(SynModuleOrNamespace(_, _, moduleDeclarations, _, _, _, _)) -> 
            moduleDeclarations |> List.map ModuleDeclaration
        | Binding(SynBinding.Binding(_, _, _, _, _, _, _, pattern, _, expression, _, _)) -> 
            [Pattern pattern; Expression expression]
        | ExceptionDefinition(ExceptionDefn(exceptionRepresentation, members, _)) -> 
            ExceptionRepresentation exceptionRepresentation::(members |> List.map MemberDefinition)
        | ExceptionRepresentation(ExceptionDefnRepr(_, unionCase, _, _, _, _)) -> [UnionCase unionCase]
        | TypeDefinition(TypeDefn(componentInfo, typeRepresentation, members, _)) -> 
            ComponentInfo componentInfo::TypeRepresentation typeRepresentation::(members |> List.map MemberDefinition)
        | TypeSimpleRepresentation(x) -> typeSimpleRepresentationChildren x
        | Type(x) -> typeChildren x
        | Match(x) -> matchChildren x
        | MemberDefinition(x) -> memberDefinitionChildren x
        | Field(SynField.Field(_, _, _, synType, _, _, _, _)) -> [Type synType]
        | Pattern(x) -> patternChildren x
        | ConstructorArguments(x) -> constructorArgumentsChildren x
        | Expression(x) -> expressionChildren x
        | SimplePattern(x) -> simplePatternChildren x
        | SimplePatterns(x) -> simplePatternsChildren x
        | InterfaceImplementation(InterfaceImpl(synType, bindings, _)) -> 
            Type synType::(bindings |> List.map Binding)
        | TypeRepresentation(x) -> typeRepresentationChildren x
        | FuncApp(exprs) -> exprs |> List.map Expression
        | Lambda(args, body) -> [yield! args |> List.map LambdaArg; yield LambdaBody(body)]
        | LambdaBody(LambdaBody.LambdaBody(body)) -> [Expression(body)]
        | LambdaArg(LambdaArg.LambdaArg(arg)) -> [SimplePatterns(arg)]

        | If(cond, body, elseIfs, Some(elseBody)) -> 
            [ yield Expression(cond); yield Expression(body); 
              for (elseIfCond, elseIfBody) in elseIfs do 
                  yield Expression(elseIfCond)
                  yield Expression(elseIfBody)
              yield Expression(elseBody) ]
        | If(cond, body, elseIfs, None) -> 
            [ yield Expression(cond); yield Expression(body); 
              for (elseIfCond, elseIfBody) in elseIfs do 
                  yield Expression(elseIfCond)
                  yield Expression(elseIfBody) ]

        | ComponentInfo(_)
        | EnumCase(_)
        | UnionCase(_)
        | Identifier(_)
        | TypeParameter(_) -> []

module AbstractSyntaxArray =

    open System.Collections.Generic
    open Ast
    open Microsoft.FSharp.Compiler.Ast

    type SyntaxNode =
        | Identifier = 1uy
        | Constant = 2uy
        | Null = 3uy
        | Expression = 4uy
        | FuncApp = 5uy

        | If = 10uy

        | Lambda = 20uy
        | LambdaArg = 21uy
        | LambdaBody = 22uy

        | ArrayOrList = 30uy
        | Tuple = 31uy

        | Other = 255uy

    let private astNodeToSyntaxNode = function
        | Expression(SynExpr.Null(_)) -> SyntaxNode.Null
        | Expression(SynExpr.Tuple(_)) -> SyntaxNode.Tuple
        | Expression(SynExpr.ArrayOrListOfSeqExpr(_))
        | Expression(SynExpr.ArrayOrList(_)) -> SyntaxNode.ArrayOrList
        | Expression(SynExpr.Const(_)) -> SyntaxNode.Constant
        | Lambda(_) -> SyntaxNode.Lambda
        | LambdaArg(_) -> SyntaxNode.LambdaArg
        | LambdaBody(_) -> SyntaxNode.LambdaBody
        | FuncApp(_) -> SyntaxNode.FuncApp
        | Identifier(_) -> SyntaxNode.Identifier

        | Expression(SynExpr.Ident(_) | SynExpr.LongIdent(_) | SynExpr.LongIdentSet(_) | SynExpr.App(_) | SynExpr.Lambda(_)) -> SyntaxNode.Other

        | Expression(_) -> SyntaxNode.Expression
        | If(_) -> SyntaxNode.If

        | ModuleOrNamespace(_)
        | ModuleDeclaration(_)
        | AstNode.Binding(_)
        | ExceptionDefinition(_)
        | ExceptionRepresentation(_)
        | TypeDefinition(_)
        | TypeSimpleRepresentation(_)
        | AstNode.Field(_)
        | Type(_)
        | Match(_)
        | TypeParameter(_)
        | MemberDefinition(_)
        | Pattern(_)
        | ConstructorArguments(_)
        | SimplePattern(_)
        | SimplePatterns(_)
        | InterfaceImplementation(_)
        | TypeRepresentation(_)
        | AstNode.ComponentInfo(_)
        | AstNode.EnumCase(_)
        | AstNode.UnionCase(_)
        | AstNode.If(_) -> SyntaxNode.Other

    [<Struct>]
    type Node(syntaxNode: SyntaxNode, identifierHashCode: int, actual: AstNode) = 
        member __.SyntaxNode = syntaxNode
        member __.Identifier = identifierHashCode
        member __.Actual = actual

    [<Struct>]
    type private PossibleSkip(skipPosition: int, depth: uint16) = 
        member __.SkipPosition = skipPosition
        member __.Depth = depth

    [<Struct>]
    type private StackedNode(node: AstNode, depth: uint16) = 
        member __.Node = node
        member __.Depth = depth
        
    let astToArray hint =
        let astRoot =
            match hint with
            | ParsedInput.ImplFile(ParsedImplFileInput(_,_,_,_,_,moduleOrNamespaces,_)) -> 
                ModuleOrNamespace moduleOrNamespaces.[0]
            | ParsedInput.SigFile _ -> failwith "Expected implementation file."
    
        let nodes = List<_>()
        let left = Stack<_>()
        let possibleSkips = Stack<PossibleSkip>()
        let skips = Dictionary<_, _>()

        let inline tryAddPossibleSkips depth =
            while possibleSkips.Count > 0 && possibleSkips.Peek().Depth >= depth do
                let nodePosition = possibleSkips.Pop().SkipPosition
                let numberOfChildren = nodes.Count - nodePosition - 1
                if numberOfChildren > 0 then
                    skips.Add(nodePosition, numberOfChildren)

        left.Push (StackedNode(astRoot, 0us))

        while left.Count > 0 do
            let stackedNode = left.Pop()
            let astNode = stackedNode.Node
            let depth = stackedNode.Depth
        
            tryAddPossibleSkips depth

            let children = AstTemp.traverseNode astNode
            children |> List.rev |> List.iter (fun node -> left.Push (StackedNode(node, depth + 1us)))

            let hashCode = 
                match astNode with 
                | Identifier(ident) -> ident.GetHashCode()
                | _ -> 0

            match astNodeToSyntaxNode astNode with
            | SyntaxNode.Other -> ()
            | syntaxNode -> 
                if not children.IsEmpty then
                    possibleSkips.Push (PossibleSkip(nodes.Count, depth))
                nodes.Add (Node(syntaxNode, hashCode, astNode))
        
        tryAddPossibleSkips 0us

        let nodes = nodes.ToArray()
    
        let skipArray = Array.zeroCreate nodes.Length
    
        for entry in skips do
            skipArray.[entry.Key] <- entry.Value

        (nodes, skipArray)