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
        
    /// Inlines pipe operators to give a flat function application expression
    /// e.g. `x |> List.map id` to `List.map id x`. 
    let (|FuncApp|_|) functionApplication =
        let rec flatten flattened exprToFlatten =
            match exprToFlatten with
            | SynExpr.App(_, _, x, y, _) -> 
                match removeParens x with
                | SynExpr.App(_, true, SynExpr.Ident(op), rhs, _) as app ->
                    let lhs = removeParens y

                    match op.idText with
                    | "op_PipeRight" | "op_PipeRight2" | "op_PipeRight3" -> 
                        flatten [removeParens rhs] lhs
                    | "op_PipeLeft" | "op_PipeLeft2" | "op_PipeLeft3" -> 
                        flatten (removeParens lhs::flattened) (removeParens rhs)
                    | _ -> flatten (removeParens lhs::flattened) app
                | x -> 
                    let leftExpr, rightExpr = (x, removeParens y)
                    flatten (removeParens rightExpr::flattened) leftExpr
            | expr -> (removeParens expr)::flattened

        match functionApplication with
        | AstNode.Expression(SynExpr.App(_, _, _, _, range) as functionApplication) -> 
            Some(flatten [] functionApplication, range)
        | _ -> None

    type Lambda = { Arguments: SynSimplePats list; Body: SynExpr }

    let (|Lambda|_|) lambda = 
        /// A match clause is generated by the compiler for each wildcard argument, 
        /// this function extracts the body expression of the lambda from those statements.
        let rec removeAutoGeneratedMatchesFromLambda = function
            | SynExpr.Match(SequencePointInfoForBinding.NoSequencePointAtInvisibleBinding, 
                            _, 
                            [SynMatchClause.Clause(SynPat.Wild(_), _, expr, _, _)], _, _) ->
                removeAutoGeneratedMatchesFromLambda expr
            | x -> x

        let (|IsCurriedLambda|_|) = function
            | SynExpr.Lambda(_, _, parameter, (SynExpr.Lambda(_) as inner), _) as outer 
                    when outer.Range = inner.Range ->
                Some(parameter, inner)
            | _ -> None

        let rec getLambdaParametersAndExpression parameters = function
            | IsCurriedLambda(parameter, curriedLambda) ->
                getLambdaParametersAndExpression (parameter::parameters) curriedLambda
            | SynExpr.Lambda(_, _, parameter, body, _) ->
                { Arguments = parameter::parameters |> List.rev
                  Body = removeAutoGeneratedMatchesFromLambda body } |> Some
            | _ -> None

        match lambda with
        | AstNode.Expression(SynExpr.Lambda(_, _, _, _, range) as lambda) -> 
            getLambdaParametersAndExpression [] lambda 
            |> Option.map (fun x -> (x, range))
        | _ -> None

    let (|Cons|_|) pattern =
        match pattern with
        | SynPat.LongIdent(LongIdentWithDots([identifier], _), 
                           _, _,
                           Pats([SynPat.Tuple([lhs; rhs], _)]), _, _) 
                when identifier.idText = "op_ColonColon" ->
            Some(lhs, rhs)
        | _ -> None

    type ExtraSyntaxInfo =
        | LambdaArg = 1uy
        | LambdaBody = 2uy
        | Else = 3uy
        | None = 255uy

    [<Struct>]
    type Node(extraInfo:ExtraSyntaxInfo, astNode:Ast.AstNode) = 
        member __.ExtraSyntaxInfo = extraInfo
        member __.AstNode = astNode

    let inline Expression x = Node(ExtraSyntaxInfo.None, Expression x)
    let inline Pattern x = Node(ExtraSyntaxInfo.None, Pattern x)
    let inline SimplePattern x = Node(ExtraSyntaxInfo.None, SimplePattern x)
    let inline SimplePatterns x = Node(ExtraSyntaxInfo.None, SimplePatterns x)
    let inline ModuleOrNamespace x = Node(ExtraSyntaxInfo.None, ModuleOrNamespace x)
    let inline ModuleDeclaration x = Node(ExtraSyntaxInfo.None, ModuleDeclaration x)
    let inline Binding x = Node(ExtraSyntaxInfo.None, Binding x)
    let inline TypeDefinition x = Node(ExtraSyntaxInfo.None, TypeDefinition x)
    let inline MemberDefinition x = Node(ExtraSyntaxInfo.None, MemberDefinition x)
    let inline ComponentInfo x = Node(ExtraSyntaxInfo.None, ComponentInfo x)
    let inline ExceptionDefinition x = Node(ExtraSyntaxInfo.None, ExceptionDefinition x)
    let inline ExceptionRepresentation x = Node(ExtraSyntaxInfo.None, ExceptionRepresentation x)
    let inline UnionCase x = Node(ExtraSyntaxInfo.None, UnionCase x)
    let inline EnumCase x = Node(ExtraSyntaxInfo.None, EnumCase x)
    let inline TypeRepresentation x = Node(ExtraSyntaxInfo.None, TypeRepresentation x)
    let inline TypeSimpleRepresentation x = Node(ExtraSyntaxInfo.None, TypeSimpleRepresentation x)
    let inline Type x = Node(ExtraSyntaxInfo.None, Type x)
    let inline Field x = Node(ExtraSyntaxInfo.None, Field x)
    let inline Match x = Node(ExtraSyntaxInfo.None, Match x)
    let inline ConstructorArguments x = Node(ExtraSyntaxInfo.None, ConstructorArguments x)
    let inline TypeParameter x = Node(ExtraSyntaxInfo.None, TypeParameter x)
    let inline InterfaceImplementation x = Node(ExtraSyntaxInfo.None, InterfaceImplementation x)
    let inline Identifier x = Node(ExtraSyntaxInfo.None, Identifier x)

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
        | SynMemberDefn.AutoProperty(_, _, _, None, _, _, _, _, expression, _, _) -> 
            [Expression expression]

    let inline private patternChildren node =
        match node with 
        | SynPat.IsInst(synType, _) -> [Type synType]
        | SynPat.QuoteExpr(expression, _) -> [Expression expression]
        | SynPat.Typed(pattern, synType, _) -> [Pattern pattern; Type synType]
        | SynPat.Or(pattern, pattern1, _) -> [Pattern pattern; Pattern pattern1]
        | SynPat.ArrayOrList(_, patterns, _)
        | SynPat.Tuple(patterns, _)
        | SynPat.Ands(patterns, _) -> patterns |> List.map Pattern
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
        | Cons(lhs, rhs) -> 
            [Pattern lhs; Pattern rhs]
        | SynPat.LongIdent(_, _, _, constructorArguments, _, _) -> 
            [ConstructorArguments constructorArguments]

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
        | SynExpr.Ident(ident) -> [Identifier([ident.idText])]
        | SynExpr.LongIdent(_, LongIdentWithDots(ident, _), _, _) -> 
            [Identifier(ident |> List.map (fun x -> x.idText))]
        | SynExpr.IfThenElse(cond, body, Some(elseExpr), _, _, _, _) -> 
            [Expression cond; Expression body; Node(ExtraSyntaxInfo.Else, Ast.Expression elseExpr)]
        | SynExpr.IfThenElse(cond, body, None, _, _, _, _) -> [Expression cond; Expression body]
        | SynExpr.Lambda(_)
        | SynExpr.App(_) -> []

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
        | SynSimplePats.SimplePats(simplePatterns, _) -> 
            simplePatterns |> List.map SimplePattern
        | SynSimplePats.Typed(simplePatterns, synType, _) -> 
            [SimplePatterns simplePatterns; Type synType]

    let inline private simplePatternChildren node =
        match node with 
        | SynSimplePat.Typed(simplePattern, synType, _) -> 
            [SimplePattern simplePattern; Type synType]
        | SynSimplePat.Attrib(simplePattern, _, _) -> [SimplePattern simplePattern]
        | SynSimplePat.Id(identifier, _, _, _, _, _) -> [Identifier([identifier.idText])]

    let inline private matchChildren node =
        match node with 
        | Clause(pattern, Some(expression), expression1, _, _) -> 
            [Pattern pattern; Expression expression; Expression expression1]
        | Clause(pattern, None, expression1, _, _) -> 
            [Pattern pattern; Expression expression1]

    let inline private constructorArgumentsChildren node =
        match node with 
        | SynConstructorArgs.Pats(patterns) -> 
            patterns |> List.map Pattern
        | SynConstructorArgs.NamePatPairs(namePatterns, _) -> 
            namePatterns |> List.map (snd >> Pattern)

    let inline private typeRepresentationChildren node =
        match node with 
        | SynTypeDefnRepr.ObjectModel(_, members, _) -> 
            members |> List.map MemberDefinition
        | SynTypeDefnRepr.Simple(typeSimpleRepresentation, _) -> 
            [TypeSimpleRepresentation typeSimpleRepresentation]

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
            ComponentInfo componentInfo::
                TypeRepresentation typeRepresentation::
                (members |> List.map MemberDefinition)
        | TypeSimpleRepresentation(x) -> typeSimpleRepresentationChildren x
        | Type(x) -> typeChildren x
        | Match(x) -> matchChildren x
        | MemberDefinition(x) -> memberDefinitionChildren x
        | Field(SynField.Field(_, _, _, synType, _, _, _, _)) -> [Type synType]
        | Pattern(x) -> patternChildren x
        | ConstructorArguments(x) -> constructorArgumentsChildren x
        | SimplePattern(x) -> simplePatternChildren x
        | SimplePatterns(x) -> simplePatternsChildren x
        | InterfaceImplementation(InterfaceImpl(synType, bindings, _)) -> 
            Type synType::(bindings |> List.map Binding)
        | TypeRepresentation(x) -> typeRepresentationChildren x
        | FuncApp(exprs, _) -> exprs |> List.map Expression
        | Lambda({ Arguments = args; Body = body }, _) -> 
            [ yield! args |> List.map (fun arg -> Node(ExtraSyntaxInfo.LambdaArg, Ast.SimplePatterns arg))
              yield Node(ExtraSyntaxInfo.LambdaBody, Ast.Expression(body)) ]
        | Expression(x) -> expressionChildren x

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
        | Null = 2uy
        | Expression = 3uy
        | FuncApp = 4uy
        | Unit = 5uy
        | AddressOf = 6uy
        
        | If = 10uy
        | Else = 11uy

        | Lambda = 20uy
        | LambdaArg = 21uy
        | LambdaBody = 22uy

        | ArrayOrList = 30uy
        | Tuple = 31uy

        | Wildcard = 41uy
            
        | ConstantBool = 51uy
        | ConstantByte = 52uy
        | ConstantChar = 53uy
        | ConstantDecimal = 54uy
        | ConstantDouble = 55uy
        | ConstantInt16 = 56uy
        | ConstantInt32 = 57uy
        | ConstantInt64 = 58uy
        | ConstantIntPtr = 59uy
        | ConstantSByte = 60uy
        | ConstantSingle = 61uy
        | ConstantString = 62uy
        | ConstantUInt16 = 63uy
        | ConstantUInt32 = 64uy
        | ConstantUInt64 = 65uy
        | ConstantUIntPtr = 66uy
        | ConstantBytes = 67uy
        
        | Cons = 101uy
        | And = 102uy
        | Or = 103uy

        | Other = 255uy

    let private constToSyntaxNode = function
        | SynConst.Unit(_) -> SyntaxNode.Unit
        | SynConst.Bool(_) -> SyntaxNode.ConstantBool
        | SynConst.Byte(_) -> SyntaxNode.ConstantByte
        | SynConst.Bytes(_) -> SyntaxNode.ConstantBytes
        | SynConst.Char(_) -> SyntaxNode.ConstantChar
        | SynConst.Decimal(_) -> SyntaxNode.ConstantDecimal
        | SynConst.Double(_) -> SyntaxNode.ConstantDouble
        | SynConst.Int16(_) -> SyntaxNode.ConstantInt16
        | SynConst.Int32(_) -> SyntaxNode.ConstantInt32
        | SynConst.Int64(_) -> SyntaxNode.ConstantInt64
        | SynConst.IntPtr(_) -> SyntaxNode.ConstantIntPtr
        | SynConst.SByte(_) -> SyntaxNode.ConstantSByte
        | SynConst.Single(_) -> SyntaxNode.ConstantSingle
        | SynConst.String(_) -> SyntaxNode.ConstantString
        | SynConst.UInt16(_) -> SyntaxNode.ConstantUInt16
        | SynConst.UInt32(_) -> SyntaxNode.ConstantUInt32
        | SynConst.UInt64(_) -> SyntaxNode.ConstantUInt64
        | SynConst.UIntPtr(_) -> SyntaxNode.ConstantUIntPtr
        | SynConst.UInt16s(_)
        | SynConst.UserNum(_)
        | SynConst.Measure(_) -> SyntaxNode.Other
        
    let private astNodeToSyntaxNode = function
        | Expression(SynExpr.Null(_)) -> SyntaxNode.Null
        | Expression(SynExpr.Tuple(_)) -> SyntaxNode.Tuple
        | Expression(SynExpr.ArrayOrListOfSeqExpr(_))
        | Expression(SynExpr.ArrayOrList(_)) -> SyntaxNode.ArrayOrList
        | Expression(SynExpr.AddressOf(_)) -> SyntaxNode.AddressOf
        | Identifier(_) -> SyntaxNode.Identifier
        | Expression(SynExpr.App(_)) -> SyntaxNode.FuncApp
        | Expression(SynExpr.Lambda(_)) -> SyntaxNode.Lambda
        | Expression(SynExpr.IfThenElse(_)) -> SyntaxNode.If
        | Expression(SynExpr.Const(constant, _)) -> constToSyntaxNode constant
        | Expression(SynExpr.Ident(_) | SynExpr.LongIdent(_) | SynExpr.LongIdentSet(_)) -> SyntaxNode.Other
        | Expression(_) -> SyntaxNode.Expression
        | Pattern(SynPat.Ands(_)) -> SyntaxNode.And
        | Pattern(SynPat.Or(_)) -> SyntaxNode.Or
        | Pattern(AstTemp.Cons(_)) -> 
            SyntaxNode.Cons
        | Pattern(SynPat.Wild(_)) -> SyntaxNode.Wildcard
        | Pattern(SynPat.Const(constant, _)) -> constToSyntaxNode constant
        | Pattern(SynPat.ArrayOrList(_)) -> SyntaxNode.ArrayOrList
        | Pattern(SynPat.Tuple(_)) -> SyntaxNode.Tuple
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
        | AstNode.UnionCase(_) -> SyntaxNode.Other

    [<Struct>]
    type Node(hashcode: int, actual: AstNode) = 
        member __.Hashcode = hashcode
        member __.Actual = actual

    [<Struct>]
    type private PossibleSkip(skipPosition: int, depth: int) = 
        member __.SkipPosition = skipPosition
        member __.Depth = depth

    let private getHashCode node = 
        match node with
        | Identifier(x) when (List.isEmpty >> not) x -> x |> Seq.last |> hash
        | Pattern(SynPat.Const(SynConst.Bool(x), _))
        | Expression(SynExpr.Const(SynConst.Bool(x), _)) -> hash x
        | Pattern(SynPat.Const(SynConst.Byte(x), _))
        | Expression(SynExpr.Const(SynConst.Byte(x), _)) -> hash x
        | Pattern(SynPat.Const(SynConst.Bytes(x, _), _))
        | Expression(SynExpr.Const(SynConst.Bytes(x, _), _)) -> hash x
        | Pattern(SynPat.Const(SynConst.Char(x), _))
        | Expression(SynExpr.Const(SynConst.Char(x), _)) -> hash x
        | Pattern(SynPat.Const(SynConst.Decimal(x), _))
        | Expression(SynExpr.Const(SynConst.Decimal(x), _)) -> hash x
        | Pattern(SynPat.Const(SynConst.Double(x), _))
        | Expression(SynExpr.Const(SynConst.Double(x), _)) -> hash x
        | Pattern(SynPat.Const(SynConst.Int16(x), _))
        | Expression(SynExpr.Const(SynConst.Int16(x), _)) -> hash x
        | Pattern(SynPat.Const(SynConst.Int32(x), _))
        | Expression(SynExpr.Const(SynConst.Int32(x), _)) -> hash x
        | Pattern(SynPat.Const(SynConst.Int64(x), _))
        | Expression(SynExpr.Const(SynConst.Int64(x), _)) -> hash x
        | Pattern(SynPat.Const(SynConst.IntPtr(x), _))
        | Expression(SynExpr.Const(SynConst.IntPtr(x), _)) -> hash x
        | Pattern(SynPat.Const(SynConst.SByte(x), _))
        | Expression(SynExpr.Const(SynConst.SByte(x), _)) -> hash x
        | Pattern(SynPat.Const(SynConst.Single(x), _))
        | Expression(SynExpr.Const(SynConst.Single(x), _)) -> hash x
        | Pattern(SynPat.Const(SynConst.String(x, _), _))
        | Expression(SynExpr.Const(SynConst.String(x, _), _)) -> hash x
        | Pattern(SynPat.Const(SynConst.UInt16(x), _))
        | Expression(SynExpr.Const(SynConst.UInt16(x), _)) -> hash x
        | Pattern(SynPat.Const(SynConst.UInt16s(x), _))
        | Expression(SynExpr.Const(SynConst.UInt16s(x), _)) -> hash x
        | Pattern(SynPat.Const(SynConst.UInt32(x), _))
        | Expression(SynExpr.Const(SynConst.UInt32(x), _)) -> hash x
        | Pattern(SynPat.Const(SynConst.UInt64(x), _))
        | Expression(SynExpr.Const(SynConst.UInt64(x), _)) -> hash x
        | Pattern(SynPat.Const(SynConst.UIntPtr(x), _))
        | Expression(SynExpr.Const(SynConst.UIntPtr(x), _)) -> hash x
        | _ -> 0

    [<Struct>]
    type private StackedNode(node: AstTemp.Node, depth: int) = 
        member __.Node = node
        member __.Depth = depth

    [<Struct>]
    type Skip(numberOfChildren: int, parentIndex: int) = 
        member __.NumberOfChildren = numberOfChildren
        member __.ParentIndex = parentIndex

    /// Keep index of position so skip array can be created in the correct order.
    [<Struct>]
    type private TempSkip(numberOfChildren: int, parentIndex: int, index: int) = 
        member __.NumberOfChildren = numberOfChildren
        member __.Index = index
        member __.ParentIndex = parentIndex
        
    let astToArray hint =
        let astRoot =
            match hint with
            | ParsedInput.ImplFile(ParsedImplFileInput(_,_,_,_,_,moduleOrNamespaces,_)) -> 
                ModuleOrNamespace moduleOrNamespaces.[0]
            | ParsedInput.SigFile _ -> failwith "Expected implementation file."
    
        let nodes = List<_>()
        let left = Stack<_>()
        let possibleSkips = Stack<PossibleSkip>()
        let skips = List<_>()

        let tryAddPossibleSkips depth =
            while possibleSkips.Count > 0 && possibleSkips.Peek().Depth >= depth do
                let nodePosition = possibleSkips.Pop().SkipPosition
                let numberOfChildren = nodes.Count - nodePosition - 1
                let parentIndex = if possibleSkips.Count > 0 then possibleSkips.Peek().SkipPosition else 0
                skips.Add(TempSkip(numberOfChildren, parentIndex, nodePosition))

        left.Push (StackedNode(AstTemp.Node(AstTemp.ExtraSyntaxInfo.None, astRoot), 0))

        while left.Count > 0 do
            let stackedNode = left.Pop()
            let astNode = stackedNode.Node.AstNode
            let depth = stackedNode.Depth
        
            tryAddPossibleSkips depth

            let children = AstTemp.traverseNode astNode
            children |> List.rev |> List.iter (fun node -> left.Push (StackedNode(node, depth + 1)))

            if stackedNode.Node.ExtraSyntaxInfo <> AstTemp.ExtraSyntaxInfo.None then
                possibleSkips.Push (PossibleSkip(nodes.Count, depth))

                let syntaxNode =
                    match stackedNode.Node.ExtraSyntaxInfo with
                    | AstTemp.ExtraSyntaxInfo.LambdaArg -> SyntaxNode.LambdaArg
                    | AstTemp.ExtraSyntaxInfo.LambdaBody -> SyntaxNode.LambdaBody
                    | AstTemp.ExtraSyntaxInfo.Else -> SyntaxNode.Else
                    | _ -> failwith ("Unknown extra syntax info: " + string stackedNode.Node.ExtraSyntaxInfo)

                nodes.Add (Node(Utilities.hash2 syntaxNode 0, astNode))

            match astNodeToSyntaxNode stackedNode.Node.AstNode with
            | SyntaxNode.Other -> ()
            | syntaxNode -> 
                possibleSkips.Push (PossibleSkip(nodes.Count, depth))

                nodes.Add (Node(Utilities.hash2 syntaxNode (getHashCode astNode), astNode))
        
        tryAddPossibleSkips 0

        let skipArray = Array.zeroCreate skips.Count

        let mutable i = 0
        while i < skips.Count do
            let skip = skips.[i]
            skipArray.[skip.Index] <- Skip(skip.NumberOfChildren, skip.ParentIndex)

            i <- i + 1

        (nodes.ToArray(), skipArray)