#r"packages/FSharp.Data/lib/net40/FSharp.Data.dll"
#r"packages/Suave/lib/net40/Suave.dll"
#load"async.fs"
#load"config.fsx"
open System
open System.Collections.Generic
open System.Text.RegularExpressions
open System.Web
open FSharp.Data
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Sockets
open Suave.Sockets.Control
open Suave.Sockets.AsyncSocket
open Suave.WebSocket
open Suave.Utils
open AsyncHelpers
open Config

//type to model things happening somewhere, at some time.
type Event =
    { Occurred : DateTime
      Name : string
      Lat : float
      Long : float 
      ImgSrc : string
    }

type BanksyScraper = HtmlProvider<"https://www.canvasartrocks.com/blogs/posts/70529347-121-amazing-banksy-graffiti-artworks-with-locations">
let a = BanksyScraper.Load("https://www.canvasartrocks.com/blogs/posts/70529347-121-amazing-banksy-graffiti-artworks-with-locations")

let yrPattern = "(20\d{2})"
let latlongPattern = "(\d+\.\d+,-?\d+.\d+)"

let els = a.Html.CssSelect(".blog_c").[0].Descendants("P")
            |> Seq.map (fun el -> el, el.CssSelect("img"), el.CssSelect("a[target='_blank'][href*='maps']"))
            |> List.ofSeq
            |> List.filter (fun (a,b,c) -> (b.IsEmpty && c.IsEmpty) |> not)

let (|ImgPFollowedByMapP|_|) (a : HtmlNode * HtmlNode list * HtmlNode list, b: HtmlNode * HtmlNode list * HtmlNode list) =
    try
        match (a,b) with
        |(imgP,img,mapA'),(mapP,img',mapA) 
            when ((not (img.IsEmpty) && (mapA'.IsEmpty)) && ((img'.IsEmpty) && not (mapA.IsEmpty))) -> 
                let year = Regex.Match(mapP.InnerText(), yrPattern)
                let imgsrc = img.[0].AttributeValue("src")
                let latLong = Regex.Match(mapA.[0].Attribute("href").Value(), latlongPattern)
                let name = img.[0].AttributeValue("alt")
                Some (year,imgsrc,latLong,name)
        |_ -> None
    with
    |_ -> None

let (|MapPFollowedByImgP|_|) (a : HtmlNode * HtmlNode list * HtmlNode list, b: HtmlNode * HtmlNode list * HtmlNode list) =
    try
        match (a,b) with
        |(mapP,img',mapA),(imgP,img,mapA')  
            when ( ( (img.IsEmpty) && not (mapA'.IsEmpty) ) && ( not (img'.IsEmpty) && (mapA.IsEmpty) ) ) ->
                let year = Regex.Match(mapP.InnerText(), yrPattern)
                let imgsrc = img.[0].AttributeValue("src")
                let latLong = Regex.Match(mapA.[0].Attribute("href").Value(), latlongPattern)
                let name = img.[0].AttributeValue("alt")
                Some (year,imgsrc,latLong,name)
        |_ -> None
    with
    |_ -> None

let banksysByYear = 
    [ for i in 1 .. 2 .. els.Length - 2 ->  printfn "%d" i ; els.[i], els.[i + 1]]
        |> List.map (fun (a,b) -> match (a,b) with
                                  |ImgPFollowedByMapP (yr,img,latlong,name) -> Some(yr,img,latlong,name)
                                  |MapPFollowedByImgP (yr,img,latlong,name) -> Some(yr,img,latlong,name)
                                  |_                                        -> None)
        |> List.filter (fun a -> a.IsSome) |> List.map (fun a -> a.Value)
        |> List.filter (fun (yr,img,latlong,name) -> yr.Success && latlong.Success)
        |> List.map    (fun (yr,img,latlong,name) -> let latlong' = latlong.Value.Split ',' |> Array.map float
                                                     {Occurred=DateTime(yr.Value |> int,1,1);ImgSrc=img; Lat=latlong'.[0];Long=latlong'.[1];Name=name})

let banksysByYear2 = 
    [ for i in 1 .. 2 .. els.Length - 2 -> els.[i], els.[i + 1] ]
        |> List.filter (fun ((imgP,img,mapA'),(mapP,img',mapA)) -> 
                 ( (img.IsEmpty) && not (mapA'.IsEmpty) ) && ( not (img'.IsEmpty) && (mapA.IsEmpty) ) )
        |> List.map ( fun ((mapP,img',mapA),(imgP,img,mapA')) -> let year = Regex.Match(mapP.InnerText(), yrPattern)
                                                                 let imgsrc = img.[0].AttributeValue("src")
                                                                 let latLong = Regex.Match(mapA.[0].Attribute("href").Value(), latlongPattern)
                                                                 let name = img.[0].AttributeValue("alt")
                                                                 year,imgsrc,latLong,name
                    )
        |> List.filter (fun (yr,img,latlong,name) -> yr.Success && latlong.Success)
        |> List.map    (fun (yr,img,latlong,name) -> let latlong' = latlong.Value.Split ',' |> Array.map float
                                                     {Occurred=DateTime(yr.Value |> int,1,1);ImgSrc=img; Lat=latlong'.[0];Long=latlong'.[1];Name=name})

let combinedBanksysByYear = banksysByYear2 @ banksysByYear |> List.distinct |> List.groupBy (fun e -> e.Occurred.Year) |> dict
