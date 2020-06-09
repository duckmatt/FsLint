﻿module FSharpLint.FunctionalTest.TestedProject

let main () =
    let x,y,z = 1,2,3

    not ((x * y) = z) |> ignore

    let meow = not (1 <> 1)

    let id = fun x -> x

    let dog = not true

    let dog = not false

    let sum = [1;2;3] |> List.fold (+) 0

    let x = true

    if x <> true then
        ()

    let x = System.Collections.ArrayList()

    if x = null then ()

    let woof = [1;2;3] |> List.sort |> List.head

    ()