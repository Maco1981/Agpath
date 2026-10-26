# AgPath

**Record as many driven paths per field as you like, alongside [AgOpenGPS](https://github.com/AgOpenGPS-Official/AgOpenGPS), without touching AgOpenGPS itself.**

AgOpenGPS keeps one recorded path per field. AgPath is a small companion window with one big
**Start / Stop recording** button. Every recording is saved as its own layer with the crop
(product) and variety, the date, the start and end times, the duration, the distance and the area.
Recordings are saved as standard **GeoJSON** and **KML** files, so they open in QGIS, Google Earth
or any web map. A built-in map page shows all of them on top of OpenStreetMap or your own base map.

**📖 [User guide](docs/GUIDE.md) · [Käyttöohje suomeksi](docs/OHJE.md)**

<p><img src="docs/images/en-main.png" width="300" alt="AgPath main window"> <img src="docs/images/en-map.jpg" width="520" alt="AgPath map"></p>

*Suomeksi alempana.*

---

## How it works

* AgPath **only listens** to the position messages that AgIO and AgOpenGPS already exchange on the
  local computer (UDP broadcast on `127.255.255.255`, the same approach as AgOpenGPS's own `GPS_Out`).
  It never sends anything to AgOpenGPS and never writes to AgOpenGPS files.
* Position: AgOpenGPS's roll-corrected antenna position (PGN 0x64). If AgOpenGPS is not running,
  AgIO's GPS position (PGN 0xD6) is used.
* Section state (PGN 0xE5 / 0xEF) can be used so that area is counted only while sections are on.
* The current field is detected from AgOpenGPS field boundaries (`Fields\*\Field.kml`).
* The working width comes from the tool selected in AgOpenGPS, or from your own setting.
* Tested with AgOpenGPS 6.8.5 (WinForms). Windows 10/11, .NET Framework 4.x (already part of Windows).

## Install

1. Download `AgPath.zip` from [Releases](../../releases) and unzip it to a folder of your choice,
   e.g. `Documents\AgPath`.
2. Start AgOpenGPS as usual, then start `AgPath.exe`.

No installer, no admin rights. All settings and data stay in the AgPath folder.

## Use

See the [user guide](docs/GUIDE.md) for step-by-step instructions with pictures.


* **Start recording / Stop recording**: the big button. While recording you see time, distance and area.
* When you stop, a save window opens:
  * choose a **product and variety** from the lists (or type new ones), **or** type your own name,
  * the **field** is filled in automatically,
  * the **working width** can still be corrected (area is recalculated immediately),
  * **Continue run** keeps recording, **Discard** throws the run away.
* **Products**: maintain the product and variety lists in advance.
* **Map**: opens the map page in your browser.
* **Compact**: shrinks the window to a small bar.
* A running recording is backed up every 15 s; after a power cut AgPath offers to continue or save it.

## Area

* **Area** = driven distance × working width.
* **Covered area** = the ground covered by the working width; overlaps are counted once.

## Files

```
Recordings\<field>\2026-10-21_0930_Chinese_cabbage_Produro.geojson
Recordings\<field>\2026-10-21_0930_Chinese_cabbage_Produro.kml
Recordings\recordings.csv          summary of all runs (opens in Excel)
```

GeoJSON properties (fixed English keys, in every language):

| key | meaning |
|---|---|
| `name`, `product`, `variety`, `field`, `notes` | text |
| `date` | `YYYY-MM-DD` |
| `start`, `end` | ISO 8601 with time zone |
| `duration` (`h:mm:ss`), `duration_s` | duration |
| `distance_m`, `width_m` | metres |
| `area_ha`, `covered_area_ha` | hectares |
| `only_sections_on` | area counted only with sections on |
| `point_times_s` | seconds from start for every coordinate |

The geometry is a `LineString`, or a `MultiLineString` if there were GPS gaps.

## Map

The **Map** button writes `map\data.js` and opens `map\index.html` in your browser. You can filter by
field, product, variety, date and text, see totals, and click a run for its details. Paths can be
drawn at their real working width.

**Share the map:** AgPath also keeps one self-contained file, `AgPath-map.html`, in the save folder.
Put the save folder in OneDrive (or any shared folder) and others can open the map on their own
computers without AgOpenGPS or AgPath. See the [guide](docs/GUIDE.md#5b-sharing-the-map).

Base map (Settings → Base map):

* **OpenStreetMap**: default, no key needed. Please respect the
  [OSM tile usage policy](https://operations.osmfoundation.org/policies/tiles/).
* **National Land Survey of Finland (Maanmittauslaitos)**: topographic map, aerial photo and property
  boundaries. Needs your own free API key from [maanmittauslaitos.fi](https://www.maanmittauslaitos.fi/en/rajapinnat/api-avaimen-ohje).
* **Custom XYZ tile address**, e.g. `https://example.com/tiles/{z}/{x}/{y}.png`, for your own country's map service.

Each user enters their own key; it is stored only in the local `settings.txt` and is never part of this repository.

## Agrineuvos (Finland)

`kirjaaja/` contains **AgPath Kirjaaja**, a separate program for the computer that has Suonentieto's Agrineuvos. It reads
the runs AgPath saved (e.g. in a shared OneDrive folder) and enters them into Agrineuvos as completed operations by
operating its windows, checking every step. See the [guide](docs/GUIDE.md#5c-entering-runs-into-agrineuvos-finland).
Not affiliated with or supported by Suonentieto.

## Language

AgPath uses the same language as AgOpenGPS (registry `HKCU\SOFTWARE\AgOpenGPS\Language`), or the one
chosen in Settings. Included: English, Suomi, Svenska, Deutsch, Nederlands, Français.
The Swedish, German, Dutch and French texts are machine-assisted: corrections are very welcome.

**Add a translation:** copy `lang\en.txt` to `lang\xx.txt` (`xx` = the AgOpenGPS language code,
e.g. `da`, `pl`, `cs`), translate the text after each `=`, and open a pull request. Keys starting with
`web.` are used on the map page.

## Build from source

Run `build.bat`. It uses the C# compiler that ships with Windows (`csc.exe`, .NET Framework 4), so
nothing needs to be installed. The source is plain C# 5 / WinForms in `src\`.

Every push is built by GitHub Actions. To publish a release with `AgPath.zip`, push a tag `v*`, or run the
**Build** workflow manually (Actions → Build → Run workflow) and enter a version such as `v1.0.1`.

## Test without a tractor

Start AgPath, press Start recording, then run `tools\TestSender.exe [lat lon]`. It sends a simulated
run (6 lanes × 80 m) the same way AgOpenGPS does.

## Disclaimer

AgPath records and displays data only; it does not steer or control anything. Use at your own risk.
AgPath is an independent project and is not affiliated with the AgOpenGPS project.

## License

Apache License 2.0 (same as AgOpenGPS), see [LICENSE](LICENSE).
Includes [Leaflet](https://leafletjs.com/) (BSD-2-Clause) in `map\`.

---

## Suomeksi

**AgPath tallentaa AgOpenGPS:n rinnalla rajattomasti ajettuja polkuja pellolle koskematta itse AgOpenGPS:ään.**

* Iso **Aloita / Lopeta tallennus** -nappi. Lopetettaessa valitaan tuote ja lajike (tai kirjoitetaan nimi),
  pelto tunnistetaan automaattisesti AgOpenGPS:n peltorajoista.
* Tallentuu polku, pinta-ala (ajettu ja katettu), päivämäärä, aloitus- ja lopetusaika, kesto ja matka
  GeoJSON- ja KML-tiedostoina sekä Excel-yhteenvetoon `recordings.csv`.
* **Kartta**-napista aukeaa karttasivu: suodatus pellon, tuotteen, lajikkeen ja päivämäärän mukaan sekä yhteenveto.
  Kartan voi jakaa esim. pomolle: tallennuskansiossa on itsenäinen `AgPath-kartta.html`, joka toimii jaetussa OneDrive-kansiossa.
  Karttapohjaksi OpenStreetMap, Maanmittauslaitoksen maastokartta/ilmakuva **omalla API-avaimella**, tai oma karttaosoite.
* Kieli on sama kuin AgOpenGPS:ssä.

**Käyttöohje:** [docs/OHJE.md](docs/OHJE.md)

**Asennus:** lataa `AgPath.zip` kohdasta [Releases](../../releases), pura se haluamaasi kansioon ja käynnistä `AgPath.exe`
AgOpenGPS:n rinnalle. Maanmittauslaitoksen kartan saa käyttöön Asetuksista omalla
[API-avaimella](https://www.maanmittauslaitos.fi/rajapinnat/api-avaimen-ohje).
