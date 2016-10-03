# Is 3D really banksy?
----------------------
Using F# to answer one of the most important questions 
facing humanity today - is 3D, out of popular beat combo Massive Attack, really banksy?

There have been recent suggestions in the press, that the identity of Banksy may in fact be 3D,
from Bristol brit-hoppers, Massive Attack, based on uncanny coincidences of banksys popping up on walls
in places where Massive Attack are playing. Some

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
Or, if you've cloned the git repo, you can just run build.cmd/sh. This will download the specified dependencies to the 'packages' directory.

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
the HTML is the same. This is a feature we will make use of in getting 
```html
    <tr>
          
          <th class="date"><span>Date</span></th>
          <th class="venue"><span>Venue</span></th>
          <th class="location"><span>Location</span></th>
          <th class="more"></th>
    </tr>
````
```html
<h2>9. Snorting Copper – London</h2>
<p style="text-align: left;">
	<img alt="Banksy Snorting Copper Policeman Curtain Street Shoreditch London" src="https://cdn.shopify.com/s/files/1/1003/7610/files/Banksy-Snorting-Copper-Photo.jpg?5516731841857925526" style="float: none;">
</p>
<p>(Image credit: 
	<a href="http://www.banksyunmasked.co.uk/" rel="nofollow">Banksy Unmasked</a>)
</p>
<p>This “Snorting Copper” stencil began appearing from 2005 .. 
	<a href="https://www.google.co.uk/maps/@51.502183,-0.116082,3a,75y,262.82h,76.02t/data=%213m4%211e1%213m2%211sCia574XguUeyJveYbhuGCw%212e0%216m1%211e1" target="_blank">Snorting Copper – approx location (Leake Street)</a>
</p>```
