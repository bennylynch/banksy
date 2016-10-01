#r"packages/FSharp.Data/lib/net40/FSharp.Data.dll"
#r"packages/Suave/lib/net40/Suave.dll"
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
let firstOrNone seq = Seq.tryFind (fun _ -> true) seq

type Event =
    { Occurred : DateTime
      Name : string
      //City : string
      Lat : float
      Long : float 
      ImgSrc : string
    }
with static member (-) (ev1, ev2) = 
        let varDays = (((ev1.Occurred - ev2.Occurred).Days) |> float) ** 2.
        let varLat  = (ev1.Lat - ev2.Lat) ** 2.
        let varLong = (ev1.Long - ev2.Long) ** 2.
        varDays + varLat + varLong |> sqrt
module Config =
    let [<Literal>] BingKey = "AptuYj2KB2MVNVuqw3b9pl33v3ba9KPTirN-2OKXLw3Y6ldA97CtBX2ibWZhN9GH"
module Bing = 
  // Use JSON provider to get a type for calling the API
  let [<Literal>] BingSample = "http://dev.virtualearth.net/REST/v1/Locations?query=Prague&includeNeighborhood=1&maxResults=5&key=" + Config.BingKey  
  type Bing = JsonProvider<BingSample>
  let cache = Dictionary<_,_>()
  /// Returns inferred location and lat/lng coordinates; cached in dict
  let locate (city:string) = 
    if cache.ContainsKey(city.ToUpper()) then 
        cache.[city.ToUpper()]
    else
        try
          let url = 
            sprintf "http://dev.virtualearth.net/REST/v1/Locations?query=%s&includeNeighborhood=1&maxResults=5&key=%s" 
                (HttpUtility.UrlEncode city)  Config.BingKey
          let bing = Bing.Load(url)
          bing.ResourceSets
            |> Seq.collect (fun r -> r.Resources)
            |> Seq.choose (fun r -> 
                match r.Point.Coordinates with
                | [| lat; lng |] -> cache.Add(city.ToUpper(), Some(float lat,float lng))
                                    Some(float lat, float lng)
                | _ -> None)
            |> Seq.tryFind (fun _ -> true)
        with e -> 
          printfn "[ERROR] Bing failed: %A" e
          None 
let loc = Bing.locate "Los angeles"
type BanksyScraper = HtmlProvider<"https://www.canvasartrocks.com/blogs/posts/70529347-121-amazing-banksy-graffiti-artworks-with-locations">
let a = BanksyScraper.Load("https://www.canvasartrocks.com/blogs/posts/70529347-121-amazing-banksy-graffiti-artworks-with-locations")


let yrPattern = "(20\d{2})"
let latlongPattern = "(\d+\.\d+,-?\d+.\d+)"

let els = a.Html.Descendants("P")
            |> Seq.map (fun el -> el, el.CssSelect("img"), el.CssSelect("a[target='_blank'][href*='maps']"))
            |> List.ofSeq
            |> List.filter (fun (a,b,c) -> (b.IsEmpty && c.IsEmpty) |> not)

let banksys= [ for i in 1 .. 2 .. els.Length - 2 -> els.[i], els.[i + 1] ]
               |> List.filter (fun ((imgP,img,mapA'),(mapP,img',mapA)) -> (not (img.IsEmpty) && (mapA'.IsEmpty)) && ((img'.IsEmpty) && not (mapA.IsEmpty)))
               |> List.map ( fun ((imgP,img,mapA'),(mapP,img',mapA)) -> let year = Regex.Match(mapP.InnerText(), yrPattern)
                                                                        let imgsrc = img.[0].AttributeValue("src")
                                                                        let latLong = Regex.Match(mapA.[0].Attribute("href").Value(), latlongPattern)
                                                                        let name = img.[0].AttributeValue("alt")
                                                                        year,imgsrc,latLong,name
                           )
               |> List.filter (fun (yr,img,latlong,name) -> yr.Success && latlong.Success)
               |> List.map    (fun (yr,img,latlong,name) -> let latlong' = latlong.Value.Split ',' |> Array.map float
                                                            {Occurred=DateTime(yr.Value |> int,1,1);ImgSrc=img; Lat=latlong'.[0];Long=latlong'.[1];Name=name})
               |> List.sortBy (fun e -> e.Occurred)
type MassiveAttackEvents = HtmlProvider<"http://www.bandsintown.com/MassiveAttack/past_events?page=10">

let englicise (s:string) =
    let repls = [('�','U');('�','O');('�','O')] |> dict
    new String(s |> Seq.map (fun c -> match repls.TryGetValue(c) with
                                      |true,v -> v
                                      |fale,_ -> c) |> Array.ofSeq)
let mattaks = 
    [for i in 1 ..13 ->
        let dates = MassiveAttackEvents.Load(sprintf "http://www.bandsintown.com/MassiveAttack/past_events?page=%d" i)
        dates.Tables.``Past Dates``.Rows |> Seq.map (fun r -> 
                        let city = r.Location.Split([|','|]).[0].ToUpper() //|> englicise
                        let country = r.Location.Split([|','|]).[1].ToUpper() //|> englicise
                        let lat,long,name = match Bing.locate city with
                                            |Some(lat,long) -> lat,long,city
                                            |None           -> match Bing.locate country with
                                                               |Some(lt,lng) -> lt,lng,country
                                                               |None         -> 0.,0.,country 
                        {Occurred = r.Date; Name = city; Lat= lat; Long = long ; ImgSrc=""})
                        |> List.ofSeq
    ] |> List.concat |> List.sortBy (fun e -> e.Occurred)
(*
let missingGeo = mattaks |> List.filter (fun e -> e.Lat = 0.)
let mataksByYear = mattaks |> List.countBy (fun e -> e.Occurred.Year)
let baknsyByYear = banksys |> List.countBy (fun e -> e.Occurred.Year)
*)
let evt = Event<string>()
let timer = new Timers.Timer(interval = 1000. , Enabled=true)
let socketHandler (webSocket : WebSocket) =
    fun cx -> socket {
        while true do
            let! evtData =
                Control.Async.AwaitEvent(evt.Publish)
                |> Suave.Sockets.SocketOp.ofAsync
            do! webSocket.send Text (ASCII.bytes evtData) true
    }
let webPart =
    choose [
        path "/events" >=> handShake socketHandler
        pathRegex "(.*)\.(css|js|htm)" >=> Files.browseHome
    ]
let config = { defaultConfig with
                   homeFolder = Some(__SOURCE_DIRECTORY__ + "/web/")
                   bindings = 
                        [ HttpBinding.mk HTTP (System.Net.IPAddress.Parse "0.0.0.0") 8082us ]
             }
startWebServerAsync config webPart
