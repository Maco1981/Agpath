# AgPath Kirjaaja

Erillinen ohjelma sille koneelle, jossa on Suonentiedon Agrineuvos. Se lukee AgPathin tallentamat ajot
(esimerkiksi OneDriven kautta jaetusta Agpath-kansiosta) ja kirjaa ne Agrineuvokseen toteutettuina tapahtumina.
AgPathia tai AgOpenGPS:ää ei tarvita tällä koneella.

1. Pura zip esimerkiksi kansioon `Tiedostot\AgPathKirjaaja` ja käynnistä `AgPathKirjaaja.exe`.
2. Valitse kansio, jossa AgPathin ajot ovat (ohjelma löytää OneDriven Agpath-kansion yleensä itse).
3. Avaa Agrineuvos ja tila. Napauta ajoa ja anna kerran Agrineuvoksen lohkotunnus ja tarvike.
4. Rastita ajot ja paina **Kirjaa valitut Agrineuvokseen**. Älä käytä hiirtä tai näppäimistöä kirjauksen aikana.

Ohjelma käyttää Agrineuvosta samoin kuin ihminen ja tarkistaa jokaisen vaiheen ennen tallennusta. Vastaavuudet ja
tieto jo kirjatuista ajoista tallentuvat ohjelman kansioon (`agrineuvos.txt`). Ei liity Suonentietoon eikä ole sen tukema.

---

A separate program for the computer that has Suonentieto's Agrineuvos: it reads the runs saved by AgPath (e.g. from a
shared OneDrive folder) and enters them into Agrineuvos as completed operations. Apache License 2.0.
