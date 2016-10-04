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
                when ((not (img.IsEmpty) && (mapA'.IsEmpty)) && ((img'.IsEmpty) && not (mapA.IsEmpty))) -> 
                let year = Regex.Match(mapP.InnerText(), yrPattern)
                let imgsrc = img.[0].AttributeValue("src")
                let latLong = Regex.Match(mapA.[0].Attribute("href").Value(), latlongPattern)
                let name = img.[0].AttributeValue("alt")
                Some (year,imgsrc,latLong,name)
        |_ -> None
    with
    |_ -> None

let banksysByYear = 
    [ for i in 1 .. 2 .. els.Length - 2 -> els.[i], els.[i + 1]]
        |> List.map (fun (a,b) -> match (a,b) with
                                  |MapPFollowedByImgP (yr,img,latlong,name) -> 
                                        printfn "2nd match"
                                        Some(yr,img,latlong,name)
                                  |ImgPFollowedByMapP (yr,img,latlong,name) -> 
                                        printfn "1st match"
                                        Some(yr,img,latlong,name)

                                  |_                                        -> None)
        |> List.filter (fun a -> a.IsSome) |> List.map (fun a -> a.Value)
        |> List.filter (fun (yr,img,latlong,name) -> yr.Success && latlong.Success)
        |> List.map    (fun (yr,img,latlong,name) -> let latlong' = latlong.Value.Split ',' |> Array.map float
                                                     {Occurred=DateTime(yr.Value |> int,1,1);ImgSrc=img; Lat=latlong'.[0];Long=latlong'.[1];Name=name})
        |> List.groupBy (fun e -> e.Occurred.Year) |> dict

type MassiveAttackScraper = HtmlProvider<"http://www.bandsintown.com/MassiveAttack/past_events?page=10">

//Iterate though 13 pages, mapping the results in to Event record type, getting coords from Bing.
//Concat, and sort by Occurred DateTime.
let mattaks = 
    [for i in 1 ..13 ->
        let dates = MassiveAttackScraper.Load(sprintf "http://www.bandsintown.com/MassiveAttack/past_events?page=%d" i)
        dates.Tables.``Past Dates``.Rows |> Seq.map (fun r -> 
                        let city = r.Location.Split([|','|]).[0].ToUpper()
                        let lat,long,name = match Bing.locate city with
                                            |Some(lat,long) -> lat,long,city
                                            |None           -> 0.,0.,city 
                        {Occurred = r.Date; Name = city; Lat= lat; Long = long ; ImgSrc="/3d.jpg"})
                        |> List.ofSeq
    ] |> List.concat |> List.sortBy (fun e -> e.Occurred)

//Suave bits

let timer = new Timers.Timer(interval = 500. , Enabled=true)
// Cached version of: https://en.wikipedia.org/wiki/List_of_time_zones_by_country
type TimeZones = HtmlProvider<"data/List_of_time_zones_by_country.html">
let reg = System.Text.RegularExpressions.Regex("""UTC([\+\-][0-9][0-9]\:[0-9][0-9])?""")

let explicitZones = 
  [ "France", "UTC+01:00"; "United Kingdom", "UTC"; "Kingdom of Denmark", "UTC+01:00"
    "Netherlands", "UTC+01:00"; "Portugal", "UTC"; "Spain", "UTC"; 
    "Northern Cyprus", "UTC+02:00"; "Denmark", "UTC+01:00"; "Greenland", "UTC-03:00"
    "North Korea", "UTC+08:30"; "South Korea", "UTC+09:00"; "New Caledonia", "UTC+11:00"
    "Somaliland", "UTC+03:00"; "Republic of Serbia", "UTC+01:00"
    "United Republic of Tanzania", "UTC+03:00"; "United States of America", "UTC-06:00" ]

/// Returns a list of pairs with country name and its time zone
let timeZones = 
 [| let special = dict explicitZones 
    for k, v in explicitZones do
      yield k, v
    for r in TimeZones.GetSample().Tables.Table1.Rows do
      if not (special.ContainsKey r.Country) then 
        let tz = r.``Time Zone``.Replace("−", "-")
        let matches = reg.Matches(tz)
        if matches.Count > 0 then
          yield r.Country, matches.[matches.Count/2].Value |] 

/// Returns a list of time zones, sorted from -7:00 to +13:00
let zoneSequence = 
  timeZones |> Seq.map snd |> Seq.distinct |> Seq.sortBy (fun s -> 
    match s.Substring(3).Split(':') with
    | [| h; m |] -> int h, int m
    | _ -> 0, 0 )
/// Use JSON type provider to generate types for the things we are returning
type JsonTypes = JsonProvider<"""{
    "socketMapEvent": 
      { "latitude":50.07, "longitude":78.43, "text":"hello", "occurred" : "01/01/00",
        "picture":"http://blah.jpg", "name":"sillyjoe" },
    "timeZoneInfo": 
      { "countries": [ {"country": "UK", "zone": "UTC" }, {"country": "UK", "zone": "UTC" } ],
        "zones": [ "UTC", "UTC+00:00" ] }
  }""">

let eventStream =
    timer.Elapsed |> Observable.scan (fun count _ -> count + 1) 0 //count will increment with each Elapsed event
                  |> Observable.map  (fun i -> let event = mattaks.[i % mattaks.Length]
                                               let yearBanksys = match banksysByYear.TryGetValue event.Occurred.Year with
                                                                 |true,bs->
                                                                       bs |> List.map (fun event ->
                                                                               JsonTypes.SocketMapEvent(event.Lat  |> decimal, 
                                                                                                        event.Long |> decimal,
                                                                                                        event.Name,
                                                                                                        event.Occurred,
                                                                                                        event.ImgSrc,
                                                                                                        "banksy").JsonValue) |> Array.ofList
                                                                 |false,_ -> [||]
                                               JsonTypes.SocketMapEvent(event.Lat  |> decimal, 
                                                                        event.Long |> decimal,
                                                                        event.Name,
                                                                        event.Occurred,
                                                                        event.ImgSrc,
                                                                        "3d").JsonValue.ToString()
                                               ,JsonValue.Array(yearBanksys).ToString()
                                     )
//split the combined stream Observable<string,string> into 2
let _,mattakGigEvents = eventStream |> Observable.map (fun (m,b) -> m)  |> Observable.start
let _,banksyEvents    = eventStream |> Observable.map (fun (m,b) -> b ) |> Observable.start

// Passes updates from IObservable<string> to a socket
let socketOfObservable (updates:IObservable<string>) (webSocket:WebSocket) cx = socket {
  while true do
    let! update = updates |> Async.AwaitObservable |> Suave.Sockets.SocketOp.ofAsync
    do! webSocket.send Text (System.Text.Encoding.UTF8.GetBytes update) true }

/// Time-zone information (calcualted just once) packaged as a JSON
let timeZonesJson = 
  (JsonTypes.TimeZoneInfo
    (Array.map (fun (c, z) -> JsonTypes.Country(c, z)) timeZones, 
     Array.ofSeq zoneSequence)).ToString()

let webPart =
    choose [
        path "/mattaks" >=> handShake ( socketOfObservable mattakGigEvents )
        path "/banksys" >=> handShake ( socketOfObservable banksyEvents )
        path "/zones" >=> Successful.OK timeZonesJson
        pathRegex "(.*)\.(css|js|html|jpg)" >=> Files.browseHome
    ]
let config = { defaultConfig with
                   homeFolder = Some(__SOURCE_DIRECTORY__ + @"\web")
                   bindings = 
                        [ HttpBinding.mk HTTP (System.Net.IPAddress.Parse "0.0.0.0") 8082us ]
             }

let start, run = startWebServerAsync config webPart
let ct = new System.Threading.CancellationTokenSource()
Async.Start(run, ct.Token)
System.Diagnostics.Process.Start("chrome.exe","http://localhost:8082/index.html")
