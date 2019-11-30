namespace FSharp.Data.Gherkin

type Feature (name:string,description:string) =
    member __.Name = name
    member __.Description = description

type Background (name:string,description:string) =
    member __.Name = name
    member __.Description = description

type Scenario (name:string,description:string) =
    member __.Name = name
    member __.Description = description

type Step (order:int,keyword:string,text:string) =
    member __.Order = order
    member __.Text = text    
    member __.Keyword = keyword    

type DataCell (header:string,value:string) =
    member __.Header = header
    member __.Value = value

module Constructors =
    let Feature = (typeof<Feature>).GetConstructors().[0]
    let Background = (typeof<Background>).GetConstructors().[0]
    let Scenario = (typeof<Scenario>).GetConstructors().[0]
    let Step = (typeof<Step>).GetConstructors().[0]
    let DataCell = (typeof<DataCell>).GetConstructors().[0]