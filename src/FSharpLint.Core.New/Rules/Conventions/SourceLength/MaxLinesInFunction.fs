module FSharpLint.Rules.MaxLinesInFunction

open FSharp.Compiler.Ast
open FSharpLint.Framework.Ast
open FSharpLint.Framework.AstInfo
open FSharpLint.Framework.Rules

let runner (config:Helper.SourceLength.Config) (args:AstNodeRuleParams) =
    match args.astNode with
    | AstNode.Binding(SynBinding.Binding(_, _, _, _, _, _, valData, _, _, _, _, _) as binding) ->
       match identifierTypeFromValData valData with
       | Function -> Helper.SourceLength.checkSourceLengthRule config binding.RangeOfBindingAndRhs "Function"
       | _ -> Array.empty
    | _ -> Array.empty

let rule config =
    { name = "MaxLinesInFunction"
      identifier = None
      ruleConfig = { AstNodeRuleConfig.runner = runner config; cleanup = ignore } }
    |> AstNodeRule