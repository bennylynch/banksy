Is 3D really banksy?
====================
##Using F# to answer one of the most important questions facing humanity today - 
##is 3D, out of popular beat combo Massive Attack, really banksy?

There have been recent suggestions in the [press](http://www.independent.co.uk/arts-entertainment/art/news/banksy-identity-theres-a-wild-theory-the-graffiti-artist-is-3d-of-massive-attack-a7222326.html), that the identity of Banksy may in fact be Robert Del Naja,
aka 3D, based on uncanny coincidences of banksy art-works appearing in places where Massive Attack are playing. 

Some time ago, academics at Queen Mary University, London, used Geoprofiling (in R, no less), to 'prove' that
the banksy was in fact Robert Gunningham, using the locations of 140 art works in London and Bristol, and locations 
Gunningham was know to have lived in.


Setting things up
-----------------
We're going to use paket for dependency management. We'll be needing FSharp.Data and Suave, so our paket.dependencies
looks like
```
source https://nuget.org/api/v2
nuget FSharp.Data
nuget Suave 
```
To download, run '.paket/paketbootstrpper.exe' (which downloads the latest paket.exe, from github), and then '.paket/paket.exe install'.
If you've cloned the git repo, you can just run build.cmd/sh. This will download the specified dependencies to the 'packages' directory.

Getting the data
----------------
To get the data, we are going to use the HTML Type Provider from FSharp.Data. As the name implies, the HTML provider makes extracting data from HTML 
a breeze (well, most of the time, as will become apparent). You just give it a static paramter to and example of the html document you wnat to use to
generate your types (this can be a local file, or more usually a URL), and then you can bind an instance of the type to a value, thus -

```fsharp
type HtmlTypes= HtmlProvider<"http://someurl.org?page=1">
let data = HtmlTypes.Load("http://someurl.org?page=2")
```

Note, that the url/path used as the static type paramter for HtmlProvider need not be the same as the one passed to Load() - as long as the structure
of the HTML is the same. This is a feature we will make use of in getting data for Massive Attack gigs. I found a very good source for this data
[here](http://www.bandsintown.com/MassiveAttack/past_events?page=1). This page contains lists of Massive attack gigs in tables, with the below headings

```html
    <tr>
          <th class="date"><span>Date</span></th>
          <th class="venue"><span>Venue</span></th>
          <th class="location"><span>Location</span></th>
          <th class="more"></th>
    </tr>
```
So, to get our list of massive attack gigs, first we define our 'MassiveAttackScraper' type:
```fsharp
type MassiveAttackScraper = HtmlProvider<"http://www.bandsintown.com/MassiveAttack/past_events?page=1">
and if we Load()
```fsharp
let dates = MassiveAttackScraper.Load("http://www.bandsintown.com/MassiveAttack/past_events?page=1")
```
we get a value with a property Tables, one of which is 'Past Dates', containing an Array of 'Row's
```fsharp
Rows = [|(11-Nov-09 12:00:00 AM, "Le Z�nith", "Paris, France Le Z�nith",
               "I Was There");
              (10-Nov-09 12:00:00 AM, "Le Z�nith", "Paris, France Le Z�nith",
               "I Was There");
              (08-Nov-09 12:00:00 AM, "Zoppas Arena",
               "San Vendemiano, Italy Zoppas Arena", "I Was There");
              (07-Nov-09 12:00:00 AM, "Palasharp", "Milano, Italy Palasharp",
               "I Was There"); ....

val it : HtmlProvider<...>.PastDates.Row =
  (11-Nov-09 12:00:00 AM {Date = 11-Nov-09 12:00:00 AM;
                          Day = 11;
                          DayOfWeek = Wednesday;
                          DayOfYear = 315;
                          Hour = 0;
                          Kind = Local;
                          Millisecond = 0;
                          Minute = 0;
                          Month = 11;
                          Second = 0;
                          Ticks = 633934944000000000L;
                          TimeOfDay = 00:00:00;
                          Year = 2009;}, "Le Z�nith",
   "Paris, France Le Z�nith", "I Was There")
```
Each Row has properties Date, Venue, Location and Column4 (based on the header rows - the 4th column has no heading).
What's more, the Date property has been inferrerd to be a DateTime! This is going to be so easy, I'm almost ashamed ...

Next on the agenda, we should define a type to model our domain - no need to go over board, something like
```fsharp
//type to model things happening somewhere, at some time.
type Event =
    { Occurred : DateTime
      Name : string
      Lat : float
      Long : float 
      ImgSrc : string
    }
```
The purpose of ImgSrc will become apparent. The location column is consistently in the form 'City, Country' - from this, we need to get 
the latitude and longitude. To do so, we will use (yes) another type provider, this time FSharp.Data.JsonProvider, talking to the Bing Maps rest API.
The api takes a url of the form
```javascript
 http://dev.virtualearth.net/REST/v1/Locations?query=[city]&includeNeighborhood=1&maxResults=5&key=[bing_maps_key], and returns 
```
returning some complicated Json. I won't go into too much of the detail of the function (stolen almost entirely from @tpetricek [here](https://github.com/tpetricek/new-year-tweets-2016/blob/master/app.fsx#L104)),
but essentially create the Bing JsonProvider type, with an example url
```fsharp
  let [<Literal>] BingSample = "http://dev.virtualearth.net/REST/v1/Locations?query=Prague&includeNeighborhood=1&maxResults=5&key=" + Config.BingKey  
  type Bing = JsonProvider<BingSample>
```
then, a function taking a parameter for the city of interest, builds the url, and Load()s the results
```fsharp
let locate (city:string) = 
    let url = 
		sprintf "http://dev.virtualearth.net/REST/v1/Locations?query=%s&includeNeighborhood=1&maxResults=5&key=%s" 
			(HttpUtility.UrlEncode city)  Config.BingKey
    let bing = Bing.Load(url)
	...
```
The bing value, will contain an array of matches, with the most confident appearing first, each of which will contain co-ordinates, as a decimal array. The function returns (float * float) option,
returning None if the API returned no results. The function also caches results, so that we don't make unnecessary calls.
Now we have everythng we need to get a list of Massive Attack gigs, mapped into our Event record type :
```fsharp
//Iterate though 13 pages, mapping the results to Event record type, getting coords from Bing.
//Concat, and sort by Occurred DateTime.
let mattaks = 
    [for i in 1 ..13 -> //... there are 13 pages
        let dates = MassiveAttackScraper.Load(sprintf "http://www.bandsintown.com/MassiveAttack/past_events?page=%d" i)
        dates.Tables.``Past Dates``.Rows |> Seq.map (fun r -> 
                        let city = r.Location.Split([|','|]).[0].ToUpper()
                        let lat,long,name = match Bing.locate city with
                                            |Some(lat,long) -> lat,long,city
                                            |None           -> 0.,0.,city 
                        {Occurred = r.Date; Name = city; Lat= lat; Long = long ; ImgSrc="/3d.jpg"})
                        |> List.ofSeq
    ] |> List.concat |> List.sortBy (fun e -> e.Occurred)
```
Now, we have gathered a list of 241 Massive Attack gigs, with exact Dates and coordinates, in a few lines ... tremendous. At this point, I was brimming with confidence, thinking I just need to find
a similar source of data for the list of banksys (of which there's bound to be a wealth, surely?), 20 minutes later, I'll be done ...

But no - try as I might, I could find no decent source data; the best I could find was [here](https://www.canvasartrocks.com/blogs/posts/70529347-121-amazing-banksy-graffiti-artworks-with-locations).
The page looks agreeable, but could hardly be described as 'structured', the list of entries in one big div, something like: 
```html
<h2>9. Snorting Copper � London</h2>
<p style="text-align: left;">
	<img alt="Banksy Snorting Copper Policeman Curtain Street Shoreditch London" src="https://cdn.shopify.com/s/files/1/1003/7610/files/Banksy-Snorting-Copper-Photo.jpg?5516731841857925526" style="float: none;">
</p>
<p>(Image credit: 
	<a href="http://www.banksyunmasked.co.uk/" rel="nofollow">Banksy Unmasked</a>)
</p>
<p>This �Snorting Copper� stencil began appearing from 2005 .. [ .. blurb .. blurb .. ]
	<a href="https://www.google.co.uk/maps/@51.502183,-0.116082,3a,75y,262.82h,76.02t/data=%213m4%211e1%213m2%211sCia574XguUeyJveYbhuGCw%212e0%216m1%211e1" target="_blank">Snorting Copper � approx location (Leake Street)</a>
</p>
```
No handy Tables, this time ... we can't even rely on each entry being wrapped in a container div, or the like. On the plus side, most of the entries have a link to google maps, that contain
co-ordinates. On the minus side, the nearest thing to a date is in the 'blurb' P, where it may or may not indicate the year in which the work appeared. To get some meaningful data out of this, is
going to require a bit more work...

So ... we can get the url for the image from the img element, the year from the 'blurb' paragraph, and the lat/long from the google maps href. 
The HtmlProvider exposes some 'DOM' style functions, such as Descendants (accepts an element name parameter), and CssSelect ( which accepts jQuery style selectors). The first we can do, is fish out
the P elements, inside the main <div class="blog_c">
```fsharp
let els = a.Html.CssSelect(".blog_c").[0].Descendants("P")
```
We're only really interested in some of these elements though; those with the nested IMG element, and those with the google maps link. We can filter out these with some CssSelect, and filtering.
```fsharp
	        |> Seq.map (fun el -> el, el.CssSelect("img"), el.CssSelect("a[target='_blank'][href*='maps']"))
            |> List.ofSeq
            |> List.filter (fun (a,b,c) -> (b.IsEmpty && c.IsEmpty) |> not)
```

This gives us a list of (HtmlNode * HtmlNode list * HtmlNode list) tuples, the original P element (from which we hope to get the year), a list of nested img tags (if any), and a list of the nested google links (if any).
If both of the lists are empty, we discard. The list will look something like

```fsharp
[(
    <p style="text-align: left;">
		<img alt="Banksy Gorilla Artist Shave Kong Graffiti - Leake Street, London" src="https://cdn.shopify.com/s/files/1/1003/7610/files/Banksy-Kong.jpg?2258386193852830659" style="float: none;" />
	</p>,
    [<img alt="Banksy Gorilla Artist Shave Kong Graffiti - Leake Street, London" src="https://cdn.shopify.com/s/files/1/1003/7610/files/Banksy-Kong.jpg?2258386193852830659" style="float: none;" />],
    []
);
(
	<p>
		Gorilla Artist or Shave Kong .. was created ... in 2008. The festival ... blurb
	</p>,
	[],
	[<a href="https://www.google.com/maps/./@51.501353,-0.114741..." target="_blank">Shave Kong Location.</a>]
); .... ]
```
With an item containing the IMG tag list as the 2nd member of the tuple, immediately followed by an item with the list of google map links as the 3rd tuple member, and the blurb P (containing the year) as the first.
We can iterate through the list 2 at a time, extracting the year and cooridnates with Regular expressions, the name from the alt attribute of the img, and the url to the image from the src attribute. 
Disappointingly, having done this, out of a potential max of 121, we end up with 22, having filtered out elements where something is missing. The reason, in part, is because the P elements are sometimes in a different order,
with the google maps P appearing before the img P. 

