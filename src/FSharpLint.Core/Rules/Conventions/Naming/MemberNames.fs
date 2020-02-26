module FSharpLint.Rules.MemberNames

open FSharp.Compiler.Ast
open FSharpLint.Framework.Ast
open FSharpLint.Framework.AstInfo
open FSharpLint.Framework.Rules
open FSharpLint.Rules.Helper.Naming

let private getMemberIdents _ = function
    | SynPat.LongIdent(longIdent, _, _, _, _, _) ->
        match List.tryLast longIdent.Lid with
        | Some(ident) when ident.idText.StartsWith "op_" ->
            // Ignore members prefixed with op_, they are a special case used for operator overloading.
            Array.empty
        | None -> Array.empty
        | Some ident -> ident |> Array.singleton
    | _ -> Array.empty

let private isImplementingInterface parents =
    parents
    |> List.exists (function
        | AstNode.MemberDefinition (SynMemberDefn.Interface _) -> true
        | _ -> false)

let private getIdentifiers (args:AstNodeRuleParams) =
    match args.astNode with
    | AstNode.Binding(SynBinding.Binding(access, _, _, _, attributes, _, valData, pattern, _, _, _, _)) ->
        let parents = args.getParents 3
        if not (isLiteral attributes) && not (isImplementingInterface parents) then
            match identifierTypeFromValData valData with
            | Member | Property ->
                getPatternIdents false getMemberIdents true pattern
            | _ -> Array.empty
        else
            Array.empty
    | AstNode.MemberDefinition(memberDef) ->
        match memberDef with
        | SynMemberDefn.AbstractSlot(SynValSig.ValSpfn(_, identifier, _, _, _, _, _, _, _, _, _), _, _) ->
            identifier |> Array.singleton
        | _ -> Array.empty
    | _ -> Array.empty

let rule config =
    { name = "MemberNames"
      identifier = Identifiers.MemberNames
      ruleConfig = { NamingRuleConfig.config = config; getIdentifiersToCheck = getIdentifiers >> addDefaults } }
    |> toAstNodeRule
    |> AstNodeRule

let newRule (config:NewNamingConfig) =
    rule
        { NamingConfig.naming = config.Naming
          underscores = config.Underscores
          prefix = config.Prefix
          suffix = config.Suffix }
