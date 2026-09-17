# Live-TV Groups: Analyse und Umsetzungsplan

Stand: 17.09.2026. Analysierter Commit: `f4c9de1622c35b816f8ef879543ac437878c9006`, Pluginstand laut Manifest/Changelog 0.2.2.0. Lokaler Checkout und GitHub-Hauptbranch stimmen bei diesem Commit überein.

## Empfehlung

Verbindliches Ziel nach der Klarstellung: **Das originale Live-TV bleibt in Web, Android TV/Fire TV und allen weiteren Clients unverändert.** Es zeigt weiterhin sämtliche für den Benutzer verfügbaren Kanäle und deren EPG nach Jellyfins normalen Regeln. Gruppenauswahl, Gruppenreihenfolge und Auswahl sichtbarer Gruppen wirken ausschließlich innerhalb **„Live-TV Gruppen“**.

„Live-TV Gruppen“ bekommt eine eigenständige Seite im Layout des Originals mit Programme, Programmführer und Sendern. Die bestehende TV-App-Filterfunktion widerspricht dieser Anforderung und wird aus dem Zielentwurf entfernt, einschließlich aktiver Bestandsfilter. File Transformation dient ausschließlich der Einbindung des eigenen Einstiegs und eigener Assets; Plugin Pages ist eine optionale Ergänzung für die eigene Seite und persönliche Einstellungen. Die bestehende originale Live-TV-Seite erhält keine Gruppensteuerung und kein Gruppen-Overlay.

Ein zusätzliches Plugin löst einen Fehler in der EPG-Datenabfrage oder Darstellung nicht automatisch. Die Daten sollten weiterhin aus Jellyfins bestehendem Live-TV-System kommen. Einen eigenen XMLTV-Importer würde ich für dieses Plugin vorerst nicht bauen.

## Was geprüft wurde

- Alle produktiven C#-Komponenten, Webskript, Stylesheet, Dashboard-Einstellungen, vorhandene Tests sowie Build- und Release-Workflows.
- Jellyfins Servercode am Tag `v12.0`, insbesondere `LiveTvManager.GetPrograms`, DTO-Erzeugung und Autorisierung.
- Jellyfin Web am Tag `v12.0` sowie die moderne Einbindung seines Guides.
- Aktueller Quellcode des offiziellen Android-TV-Clients und von Wholphin. Diese Untersuchungen beschreiben den jeweiligen Hauptbranch, nicht automatisch die auf einem konkreten Fernseher installierte Version.
- Schnittstellen und aktuelle Releases von File Transformation, Plugin Pages und Home Screen Sections; Einsatzmöglichkeiten von Dispatcharr und Threadfin.

Validierung: `dotnet test --configuration Release` erfolgreich: **35 Tests bestanden**, keine fehlgeschlagen oder übersprungen. Eine xUnit-Analyzer-Warnung in `GroupStoreTests.cs:80`. JavaScript-Syntaxprüfung mit `node --check` erfolgreich. Keine Änderungen am produktiven Plugin-Code.

Ergänzend wurde am 17.09.2026 die angemeldete Weboberfläche auf https://jelly.enkination.de in Chrome live geprüft. Die Ergebnisse stehen im folgenden Abschnitt. Native TV-Apps wurden nicht bedient; deren Verhalten bleibt gesondert zu prüfen.

## Ergänzung: Live-Test auf jelly.enkination.de

Geprüfte Installation: Jellyfin Server/Web **12.0**, Live-TV Groups **0.2.2.0**, File Transformation **3.0.1.0**, Jellyfin Enhanced **12.7.0.0**, Xtream Library **2.0.1.0**. Als aktives Gerät war Jellyfin Android TV **0.19.10** sichtbar; dies ist eine Versionsbeobachtung, kein Test dieser App. Plugin Pages und Home Screen Sections waren nicht in der installierten Pluginliste.

**Historischer Iststand beim Live-Test, vor der Klarstellung:** Alle vier globalen Optionen sind eingeschaltet: Webintegration, App-Kanal, Playlist-Abgleich und Guidefilter. Die Ausnahme lautet tatsächlich **Jellyfin Web**. File Transformation und Skript-Einbindung werden als aktiv angezeigt. Persönliche Darstellungspräferenzen fehlen derzeit.

### Funktionsprüfung

| Funktion | Live-Ergebnis |
| --- | --- |
| Gruppen-Einstieg im normalen Live-TV | Button „Gruppen“ sichtbar, eigener Bereich öffnet sich. |
| Vorhandene Gruppen | Sportsender mit 214 Sendern und Crime mit 4 Sendern geladen. |
| Crime: Sender und EPG | Logos und aktuelle Sendungen vorhanden. HTTP 200, 4 Sender und 27 Programme im ersten Zeitfenster 10:30–16:30. Drei Zeilen enthalten Sendungen; „CRIME & INVESTIGATION“ Nr. 7 hat keine Daten. Auch im normalen Guide war dessen Zeile leer. |
| Sportgruppe: EPG | HTTP 200, 214 Sender und 56 Programme im geprüften sechsstündigen Zeitraum. Timeline wird dargestellt. Viele Eventsender haben keine Daten oder Sendepausen. Kein belastbarer Leistungstest unter gleichzeitigen Benutzern. |
| Zeitnavigation | Später/früher, „Jetzt“ und Datumsauswahl funktionieren. „Morgen“ öffnete 18:00–00:00. |
| Sendungsdetails | Richtige native Detailseite öffnet sich. Browser-Zurück verliert jedoch die Gruppenansicht; Gruppe und Guide müssen erneut geöffnet werden. |
| Senderauswahl | 432 Sender geladen, Crime enthält 4 ausgewählte Sender. Speichern in der Testgruppe funktioniert. Suche hat einen bestätigten Darstellungsfehler, siehe unten. |
| Gruppenverwaltung | Temporäre Gruppe angelegt, mit zwei Sendern gespeichert, umbenannt, Senderreihenfolge per Ziehen geändert und nach erneuter Darstellung bestätigt, anschließend gelöscht. |
| Wiedergabe | RTL Crime beim ersten Gruppenstart mit HLS-HTTP-500 und Playerfehler gescheitert. Native Senderseite danach erfolgreich, erneuter Gruppenstart ebenfalls erfolgreich mit laufendem Video. Vorübergehender Streamfehler; keine reproduzierbare Pluginursache nachgewiesen. |
| Hauptnavigation „Live-TV Gruppen“ | Gruppenordner und Auswahlordner vorhanden. Crime öffnet vier Senderkarten in einer normalen Bibliotheksansicht; kein eingebautes EPG und kein originales Live-TV-Layout. |
| Globales Dashboard | Status, Werte und Erklärungen laden. Speichern globaler Einstellungen wurde nicht ausgelöst. |
| Schmaler Bildschirm | Bei 390 × 844 bleibt die Sport-Timeline bedienbar, lange laufende Sendepausen erscheinen im sichtbaren Ausschnitt aber ohne lesbare Titel/Zeit. Beschriftung beim horizontalen Scrollen verbessern. |

Die temporäre Gruppe wurde entfernt. Die ursprünglichen Gruppen und der damals aktive Guidefilter **Crime** blieben im Test erhalten. Die anschließende Klarstellung verlangt, diese Filterfunktion in der Umsetzung zu entfernen; am laufenden Server wurde hierfür noch keine Einstellung geändert. Alle Testwiedergaben wurden beendet. Keine Aufnahmen gestartet.

### Bestätigte Fehler und Prioritäten aus dem Live-Test

1. **Sendersuche: Nichttreffer bleiben sichtbar.** Bei „Crime“ tragen 428 von 432 Zeilen `hidden`, trotzdem werden alle 432 mit `display:flex` dargestellt. Ursache im eigenen Code: `.ltvg-picker-row { display: flex; }` übersteuert die Standarddarstellung von `hidden`. Eine explizite Regel `.ltvg-picker-row[hidden] { display: none; }` ergänzen und mit echten Suchbegriffen verifizieren. Dies ist ein kleiner, direkt umsetzbarer Fehlerfix.
2. **Getrennte Einstiege erklären das gemeldete EPG-Verhalten.** Das originale Web-EPG zeigt weiterhin alle Sender, weil Jellyfin Web tatsächlich ausgenommen ist. Die eigene Timeline funktioniert, ist aber nur im separaten Gruppenbereich unter Live-TV erreichbar. Der Bibliothekseintrag „Live-TV Gruppen“ besitzt keine Timeline. Die eigene Gruppenansicht muss direkt über „Live-TV Gruppen“ erreichbar werden. Der unveränderte originale Guide ist ausdrücklich gewünscht; die Filterfunktion wird entfernt und nicht durch eine andere Ausnahme ersetzt.
3. **Rückkehr aus Details verliert den Kontext.** Gruppen-ID, View, Datum, horizontale und vertikale Position in der Seitennavigation sichern und wiederherstellen. Dieser beobachtete Fehler bestätigt die Priorität der Navigation in Phase 2.
4. **Fehlende EPG-Daten sichtbar erklären.** Die leere vierte Crime-Zeile stammt im geprüften Zeitraum nicht aus einer generell defekten Gruppenabfrage. „Keine Programmdaten“ pro Sender behalten und Diagnose zur Sender-/EPG-Zuordnung ergänzen. Sport-Eventsender gesondert verständlich darstellen.
5. **Wiedergabe ohne vorschnelle Ursache verfolgen.** Den ersten HLS-500 mit Server-/Tunerprotokoll korrelieren, falls er erneut auftritt. Erfolgreiche native und wiederholte Gruppenwiedergabe sprechen gegen einen ständig defekten Gruppenstart. `LiveStreams/MediaInfo` lieferte in beiden Einstiegen 404, obwohl spätere Wiedergabe funktionierte.

Zusätzlich wurden WebSocket-403 und ein 401 beim Laden von xThemeSong-Präferenzen beobachtet. Diese betreffen weitere Integrationen beziehungsweise Verbindung/Authentifizierung und sind nicht als Ursache der Gruppen-EPG nachgewiesen. Eine mögliche Wechselwirkung mit Jellyfin Enhanced und anderen Webtransformationen gehört in die Regressionstests.

### Anpassung des Umsetzungsplans

- Neue verbindliche Vorstufe: Eingriffe in das originale Live-TV entfernen, einschließlich Filter für native Apps und Gruppen-Overlay im originalen Web-Live-TV. Danach die Sendersuche reparieren.
- Phase 1 beginnt nun mit einer bekannten funktionierenden Referenz: Crime, native IDs, drei EPG-Zeilen plus ein Sender ohne Daten. Datenweg robust machen statt einen ungeprüften allgemeinen EPG-Ausfall zu behaupten.
- Phase 2 umfasst ausdrücklich den Hauptnavigationseintrag „Live-TV Gruppen“, die gemeinsame originale Layoutstruktur sowie erhaltene Navigation aus Details. Sichtbare Gruppen und Standardansicht werden persönliche Einstellungen.
- Browserabnahme auf der laufenden Kombination Jellyfin/Web 12.0 mit Enhanced und File Transformation; Jellyfin 12.1 separat als Kompatibilitätsprüfung. Android TV 0.19.10 als konkrete Gerätebasis für die unveränderte originale Live-TV-Ansicht und die separaten Gruppen-/Playlist-Zugänge aufnehmen.
- Aufnahme, Playlist-Abgleich in Zielapps, App-Kanal-Wiedergabe, Benutzertrennung mit zweitem Konto, Neustartwirkung und Gerätecache bleiben offene Integrationsprüfungen. Der Browsertest belegt sie nicht.

## Gewünschte Ansicht nach der Rückmeldung

Die Klarstellung legt eine strikte Trennung fest: **Gruppen-EPG ausschließlich in „Live-TV Gruppen“; originales Live-TV weiterhin mit allen Kanälen.** Das Original dient als Layoutreferenz. Seine Senderliste, Programmanfragen, Sortierung, Filter und Navigation werden nicht durch eine Gruppenwahl verändert.

Die derzeit funktionierende Timeline hinter „Live TV → Gruppen → Programm“ wird in den eigenständigen Gruppenbereich überführt. Der derzeitige Kanalordner-Einstieg wird im Web durch die eigene Gruppenansicht ersetzt beziehungsweise eindeutig dorthin geführt. Native Clients ohne Unterstützung eigener Pluginseiten behalten einen gesonderten Gruppen-/Playlist-Zugang; ein EPG im Original-Live-TV wird dort nicht als Ersatz angeboten.

Die neue Gruppenansicht übernimmt Hierarchie, Abstände, Senderkarten, Programmzeilen, Zeitleiste, Navigation und Theme aus dem originalen Live-TV. Die drei Kernansichten entsprechen den Jellyfin-Views:

1. **Programme:** „Jetzt läuft“ und kommende Sendungen für den gewählten Gruppenbereich.
2. **Programmführer:** Vollwertige EPG-Zeitleiste im Layout des Originals, mit Sendern links und Datum/Uhrzeit sowie Sendungen rechts.
3. **Sender:** Senderkarten mit Logos, laufender Sendung und nativer Live-TV-Wiedergabe.

Darüber steht eine Gruppenauswahl. Derselbe aufgelöste Senderbereich gilt für die drei Ansichten innerhalb „Live-TV Gruppen“. Ein Gruppenwechsel ändert nur dort Programme, Guide und Sender gemeinsam. Gruppenreihenfolge und Senderreihenfolge bleiben erhalten.

Zwei persönliche Einstellungen erfüllen die gewünschte Auswahl:

- **Sichtbare Gruppen:** Welche eigenen Gruppen erscheinen in Navigation und Auswahl? Ausblenden löscht nichts und verändert keine Senderberechtigung.
- **Aktueller Gruppenbereich:** Eine Gruppe, „Alle sichtbaren Gruppen“ als Vereinigung ihrer Sender oder „Alle Sender“. Die kombinierte Ansicht folgt zuerst der Gruppenreihenfolge, dann der Senderreihenfolge; doppelte Sender erscheinen beim ersten Auftreten einmal.

Diese Auslegung ist ein Umsetzungsvorschlag. Die einzelne Gruppe und auswählbare sichtbare Gruppen gehören zur ersten Version; die kombinierte Ansicht kann danach folgen. Wenn keine Gruppen sichtbar sind, bleiben „Alle Sender“ und die Gruppenverwaltung erreichbar.

„Aufnahmen“, „Geplante Aufnahmen“ und „Serienaufnahmen“ werden später passend ergänzt oder über vorhandene Jellyfin-Seiten geöffnet. Gruppenspezifisches Filtern setzt eine belastbare Senderzuordnung voraus, die bei fertigen Aufnahmen fehlen kann. Der Plan verspricht keine unechte Gruppenfilterung dieser Daten.

Referenzen: [originale Jellyfin-Web-Views 12.0](https://github.com/jellyfin/jellyfin-web/blob/v12.0/src/apps/modern/features/libraries/constants/views/livetv.ts), [originale Live-TV-Struktur](https://github.com/jellyfin/jellyfin-web/blob/v12.0/src/apps/legacy/controllers/livetv.html).

## Bestehende Architektur

| Weg | Umsetzung | Bedeutung für EPG |
| --- | --- | --- |
| Gruppen im Web | File Transformation bindet `client.js` ein; ein eigener Bereich überlagert Live-TV. | Eigene Timeline über `/LiveTvGroups/Groups/{id}/Guide`. |
| Guide in TV-Apps | Middleware verändert `GET /LiveTv/Channels`. | Die App muss diese Sender neu laden und ihre Programmanfragen daraus ableiten. |
| Gruppen unter „Kanäle“ | `GroupsChannel` erzeugt Ordner und zusätzliche Medienobjekte. | Diese Objekte sind keine regulären Live-TV-Sender mit eigener nativer Guide-Zuordnung. |
| Gruppen als Playlists | Playlist-Abgleich verwendet die Medienobjekte des App-Kanals. | Zusätzlicher Zugang zur Wiedergabe, keine eigenständige EPG-Lösung. |

Die Trennung persönlicher Gruppen pro Benutzer, die serverseitige Auflösung erlaubter Sender, die atomare Ersetzung der JSON-Dateien und die Nutzung der bestehenden Jellyfin-Programmdaten sind gute Grundlagen. Diese Struktur muss für die Verbesserung nicht vollständig ersetzt werden.

## EPG: bestätigte Befunde und offene Ursachen

### Die grundlegende Programmanfrage ist plausibel

`GroupsController.cs:279` löst zunächst die erlaubten Sender der Gruppe auf. Die Programmanfrage verwendet deren IDs und ein überlappendes Zeitfenster: `MinEndDate = from`, `MaxStartDate = to`. Genau dieses Prinzip nutzt Jellyfin Web ebenfalls. Es erfasst auch eine Sendung, die vor dem sichtbaren Zeitraum beginnt und währenddessen weiterläuft.

Auch die naheliegende Vermutung „`DtoOptions(false)` entfernt `ChannelId`“ wird vom geprüften Servercode für Jellyfin 12.0 nicht bestätigt: `DtoService` setzt `ChannelId` und `EndDate` unabhängig von optionalen Feldern; `AddInfoToProgramDto` ergänzt die Startzeit. Ein pauschaler Austausch durch alle DTO-Felder ist daher kein belegter Fix.

Belege: [Plugin-Abfrage](https://github.com/iEnki/jellyfin-plugin-livetv-groups/blob/f4c9de1622c35b816f8ef879543ac437878c9006/src/Jellyfin.Plugin.LiveTvGroups/Api/GroupsController.cs#L279), [Jellyfin 12.0 DTO-Service](https://github.com/jellyfin/jellyfin/blob/v12.0/Emby.Server.Implementations/Dto/DtoService.cs), [Jellyfin 12.0 Programmanfrage](https://github.com/jellyfin/jellyfin/blob/v12.0/src/Jellyfin.LiveTv/LiveTvManager.cs#L198), [Jellyfin Web 12.0 Guide](https://github.com/jellyfin/jellyfin-web/blob/v12.0/src/components/guide/guide.js#L330).

### Schwächen der eigenen Web-EPG

| Priorität | Bestätigter Befund | Auswirkung und Verbesserung |
| --- | --- | --- |
| Hoch | `client.js:420–432`: Sendungen werden über `ChannelId` einsortiert. Fehlende Zuordnung und tatsächlich fehlende EPG-Daten sind für den Nutzer nicht unterscheidbar. | Antwort prüfen und getrennte Zustände anzeigen: keine Sender, keine Sendungen im Zeitraum, unbrauchbare Programmdaten, Ladefehler. |
| Hoch | `client.js:422`: Die Antwortprüfung berücksichtigt Gruppe und Tab, aber keinen eindeutigen Ladeauftrag oder das aktuelle Zeitfenster. Die Senderansicht prüft den Tab nicht. | Langsame ältere Antworten können neuere Ansichten überschreiben. Ladeaufträge abbrechen und nur die neueste Antwort übernehmen; gilt für alle Ansichten. |
| Mittel | `client.js:408, 561`: Die automatische Aktualisierung lädt denselben ursprünglich gesetzten Zeitraum erneut. | Im Modus „Jetzt“ muss das Zeitfenster mitwandern; bei bewusst gewählter Vergangenheit/Zukunft bleibt es stehen. |
| Mittel | `client.js:439–443, 867`: Tage und Abendzeit entstehen durch feste Millisekundenabstände. | An Sommer-/Winterzeitwechseln können Datumsauswahl und Abendzeit falsch liegen. Lokale Kalendertage mit `setDate` und Uhrzeiten mit `setHours` berechnen. |
| Mittel | Jeder EPG-Refresh ersetzt den gesamten Inhalt; erhalten bleibt nur die horizontale Scrollposition. | Tastaturfokus und vertikale Position können verloren gehen. Zustand wiederherstellen und möglichst nur geänderte Daten aktualisieren. |
| Mittel | Die Oberfläche nutzt selbst berechnete Start-/Endzeiten, obwohl der Server normalisierte Zeiten zurückgibt. | Die zurückgegebenen Grenzen als Grundlage nehmen; Datumseingaben eindeutig mit Zeitzone validieren. |
| Mittel | Sieben Tage, Zoom und Fenstergöße sind fest im Skript hinterlegt. | Vorhandenen Guide-Zeitraum berücksichtigen, Zoom und Ansicht als persönliche Präferenzen anbieten. |

Bei dauerhaft leerem Web-EPG sind zuerst drei Fragen zu beantworten: Enthält Jellyfins normaler Guide dieselben Sender und Sendungen? Liefert der Gruppen-Endpunkt Programme? Passen deren `ChannelId`, Start- und Endzeiten zu den aufgelösten Sendern und zum sichtbaren Zeitraum? Erst diese Gegenprobe entscheidet, ob Import/Mapping, Plugin-Abfrage oder Darstellung betroffen ist.

### Bestehender TV-App-Filter: Befund und Entfernung im Zielentwurf

**Die folgenden Punkte dokumentieren den bisherigen Code; diese Funktion gehört nach der Klarstellung nicht mehr zur geplanten Lösung.** Die Middleware bearbeitet ausschließlich `GET /LiveTv/Channels`. Das ist **nicht automatisch ein Fehler**: Der offizielle Android-TV-Client lädt die Sender und fragt Programme mit deren IDs ab. Auch Wholphin lädt Sender über diese Route; seine Programme kommen allerdings über **`POST /LiveTv/Programs`** mit einem `GetProgramsDto`.

Ein Wechsel der Gruppe auf dem Server erzwingt keinen neuen Abruf in der App. Android TV hält Sender in `TvManager.allChannels`; Wholphin lädt seine Sender in der Initialisierung des Live-TV-ViewModels. Ein bloßer Programmrefresh muss die Senderliste also nicht erneuern. Das ist eine plausible Ursache für „Filter gewählt, Guide bleibt unverändert“, aber ohne installierte App-Version und Laufzeitprüfung nicht als konkrete Ursache bestätigt.

Weitere bestätigte Punkte:

- `ActiveGuideGroupId` ist ausschließlich pro Benutzer gespeichert. Eine Auswahl beeinflusst alle erfassten Geräte dieses Benutzers.
- Der Filter beeinflusst die gesamte Live-TV-Senderliste der betroffenen App, nicht nur den Bildschirm „Programmführer“.
- Das Öffnen eines Auswahlordners in `GroupsChannel.cs:183–232` speichert die aktive Gruppe. Damit kann ein lesender Ordnerabruf eine Einstellung ändern. Vorladen oder Zwischenspeichern durch Apps kann dieses Verhalten unzuverlässig machen.
- Für das nachträgliche Filtern werden `startIndex` und `limit` vor dem Aufruf der Jellyfin-Route entfernt. Bei einem Fehler in der Antwortverarbeitung wird deshalb nicht die ursprünglich paginierte Antwort zurückgegeben, sondern die zuvor erzeugte Antwort ohne diese Begrenzung. Die dokumentierte Aussage „unveränderte Originalantwort“ ist zu stark.
- Jeder gefilterte Abruf lädt zunächst alle vom ursprünglichen Request erfassten Sender und puffert die gesamte JSON-Antwort. Bei großen IPTV-Listen ist das vermeidbarer Aufwand.
- Die Ausschlussliste erfordert genaue Clientnamen. Die eigenen tokenbasierten Requests sind dabei nicht automatisch namenlos: Jellyfin 12.0 kann den Clientnamen aus dem gespeicherten Gerät zum Token ergänzen. Das sollte getestet und nicht als vermuteter Hauptfehler behandelt werden.

Belege: [Plugin-Middleware](https://github.com/iEnki/jellyfin-plugin-livetv-groups/blob/f4c9de1622c35b816f8ef879543ac437878c9006/src/Jellyfin.Plugin.LiveTvGroups/Web/GuideFilterMiddleware.cs), [Android-TV-Senderabruf und Programmanfrage](https://github.com/jellyfin/jellyfin-androidtv/blob/master/app/src/main/java/org/jellyfin/androidtv/ui/livetv/TvManagerHelper.kt), [Android-TV-Senderspeicher](https://github.com/jellyfin/jellyfin-androidtv/blob/master/app/src/main/java/org/jellyfin/androidtv/ui/livetv/TvManager.java), [Wholphin Live-TV](https://github.com/damontecres/Wholphin/blob/main/app/src/main/java/com/github/damontecres/wholphin/ui/detail/livetv/LiveTvViewModel.kt), [Jellyfin Autorisierung](https://github.com/jellyfin/jellyfin/blob/v12.0/Jellyfin.Server.Implementations/Security/AuthorizationContext.cs).

## Weitere funktionale Verbesserungen

| Bereich | Codebefund | Vorschlag |
| --- | --- | --- |
| Sender nach Neuimport | `ChannelMatcher.cs:46` nimmt den ersten Treffer bei gleichem Namen und gleicher Nummer, auch wenn mehrere identische Kandidaten existieren. | Nur eindeutige Treffer automatisch übernehmen; mehrdeutige Zuordnungen im Reparaturdialog zeigen. Quell-/Tuneridentität und stabile Provider-ID ergänzen, soweit Jellyfin sie zuverlässig bereitstellt. |
| Teilweise Reparatur | `GroupService.cs:78` schreibt reparierte Referenzen nur zurück, wenn alle Sender gefunden wurden. | Erfolgreiche Reparaturen einzeln speichern, fehlende Referenzen erhalten und sichtbar machen; konkurrierende Benutzeränderungen dabei berücksichtigen. |
| Gleichzeitige Bearbeitung | Speicherupdates sind gesperrt, aber die HTTP-API verwendet `Revision` nicht als Konfliktschutz. | Revision/ETag beim Speichern prüfen; veraltete Bearbeitung mit verständlichem Konflikthinweis ablehnen. Die Sperre schützt nicht vor zwei nacheinander gespeicherten veralteten Senderlisten. |
| API-Eingaben | Doppelte IDs bei Gruppenreihenfolge können `ToDictionary` scheitern lassen; Namenslänge ist nur im Web begrenzt. | Servervalidierung und nachvollziehbare 400-Antworten; Grenzen für Namen und Listen vereinheitlichen. |
| Speicherung | Beschädigtes JSON löst beim Laden eine Ausnahme aus. | Sicherung und Wiederherstellungsdiagnose ergänzen; beschädigte Daten erhalten und niemals still mit einem leeren Dokument überschreiben. |
| Einstellungen | Beim Laden/Speichern der Konfiguration fehlen Fehlerbehandlung und garantierte Beendigung des Ladezustands. | Erfolg, Fehler und ausstehende Änderungen sichtbar machen; abhängige Optionen erklären und validieren. |
| Wiedergabe | Der App-Kanal setzt `RequiresOpening=false` und verwendet Tunerquellen als externe Streams. | Native Live-TV-Wiedergabe bevorzugen; Alternativweg gezielt prüfen. Provider-Verbindungslimits, Stream-Ende und Berechtigungsänderungen als Integrationstests abdecken. |
| Playlists | Synchronisierung läuft im Hintergrund, für Nutzer fehlt ein eigener Status. | Letzten Erfolg/Fehler, ausstehende Änderungen und „Jetzt synchronisieren“ anzeigen. Änderungen an Einstellungen und Gruppen gezielt zusammenfassen. |

Die Einschränkung des App-Wiedergabewegs ist bereits in README und Settings erwähnt. Ein Kommentar im Code behauptet dagegen, Zugangsdaten würden nicht an Clients gelangen. `SupportsDirectPlay=false` allein garantiert das nicht, weil der Quellpfad im Wiedergabe-DTO enthalten sein kann. Dokumentation und tatsächliche PlaybackInfo-Antwort müssen gemeinsam geprüft werden.

## Bedienung und Einstellungen

### Persönliche Oberfläche

- Oben eine klar beschriftete Gruppenauswahl mit „Alle Sender“, sichtbarer aktiver Gruppe und zuletzt verwendeter Ansicht.
- Die Gruppenauswahl steuert ausschließlich Programme, Programmführer und Sender innerhalb „Live-TV Gruppen“. Sichtbare Gruppen und Standardgruppe werden persönlich gespeichert; Ausblenden löscht keine Gruppe. Es gibt keine Auswahlfunktion, die das originale Live-TV eines anderen Geräts verändert.
- Programmansichten „Jetzt/Nächste Sendung“ und „Zeitachse“. Auf kleinen Displays bietet die Listenansicht eine brauchbare Alternative zum breiten Raster.
- Tag und Uhrzeit getrennt wählen; „Jetzt“, „Heute Abend“, Zoom und manueller Refresh. Tage ohne importierte Daten eindeutig kennzeichnen.
- Sendungsdetails mit vorhandenen Jellyfin-Funktionen für Wiedergabe und Aufnahme öffnen. Bei Zurückkehren Gruppe, Zeitfenster und Fokus erhalten.
- Senderauswahl: nur ausgewählte Sender anzeigen, Suchtreffer gesammelt hinzufügen/entfernen, sichtbarer EPG-Status pro Sender.
- Gruppen duplizieren, JSON-Export/Import mit Vorschau sowie optional vom Administrator bereitgestellte Vorlagen. Import übernimmt ausschließlich für den Benutzer erlaubte Sender.
- Sortieren auch mit „Nach oben/unten“, Tastatur und Touch; Drag-and-drop bleibt eine zusätzliche Möglichkeit.
- Dialoge mit Fokusbegrenzung, Fokuswiederherstellung und vollständigem Abbrechen; Texte übersetzbar und Farben aus Jellyfins Thema übernehmen.

### Administratoreinstellungen

Globale Integrationen, persönliche Präferenzen und Diagnose sollten getrennte Abschnitte bekommen. Globale Einstellungen bleiben im Dashboard; persönliche Einstellungen liegen bei den Gruppen.

| Einstellung | Empfohlene Wirkung |
| --- | --- |
| Webintegration | Aktivieren/deaktivieren; nötigen Neustart sichtbar anzeigen. |
| Plugin-Pages-Einstieg | Automatisch anbieten, falls passende Version installiert; sonst vorhandenen Einstieg nutzen. |
| Bisheriger TV-App-Guidefilter | Entfernen; vorhandene Aktivierung darf nach dem Update keine originalen Live-TV-Anfragen mehr verändern. Alte Filterwerte nur für Datenmigration berücksichtigen. |
| App-Kanal / Playlists | Zusätzliche Zugänge explizit aktivieren; Abhängigkeit der Playlists vom App-Kanal im Formular erzwingen. |
| Diagnose | Versionen, Registrierung, tatsächlich geladene Webintegration, Gruppenauflösung, EPG-Abdeckung, letzter Playlist-Abgleich und Nachweis, dass originale Live-TV-Anfragen unbeeinflusst bleiben. |
| Erweitert | Probezeit, Cache-Laufzeit und technische Optionen nur nach Bedarf; sichere Werte voreinstellen. |

Ein Status „Transformation registriert“ beweist bisher nur den Serverteil. Ein kleiner autorisierter Clientbericht könnte zusätzlich melden, welche Skriptversion geladen wurde und ob der Einstieg gefunden wurde. Keine Tokens, Provider-URLs oder vollständigen Autorisierungsheader in Diagnoseexporte übernehmen.

## Zusammenspiel mit anderen Projekten

| Projekt | Nutzen | Empfehlung und Grenze |
| --- | --- | --- |
| [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) | Verändert ausgelieferte Webdateien, ohne Installation auf Platte zu patchen. | Beibehalten. Kleine, idempotente Einbindung und Kompatibilität mit weiteren Transformationen prüfen. |
| [Plugin Pages](https://github.com/IAmParadox27/jellyfin-plugin-pages) | Registriert nutzerseitige Seiten über `RegisterPage`; Entfernen über `RemovePage`. | Beste optionale Ergänzung für Gruppen und persönliche Settings. Verbessert den Einstieg, ersetzt weder EPG-Abfrage noch native TV-App-Oberflächen. |
| [Home Screen Sections](https://github.com/IAmParadox27/jellyfin-plugin-home-sections) | Ermöglicht registrierte Abschnitte auf der Web-Startseite. | Später optional „Jetzt in meinen Gruppen“. Kein Bestandteil des EPG-Fixes; Verhalten in nativen Apps gesondert prüfen. |
| [Dispatcharr](https://github.com/Dispatcharr/Dispatcharr) | Separater IPTV-Dienst für Quellen, EPG-Zuordnung, Proxying und M3U/XMLTV/HDHomeRun-Ausgabe. | Interessant, wenn bereits Jellyfins normaler Guide unvollständig ist oder Providerlimits zentral behandelt werden sollen. Zusätzlicher Dienst, kein Jellyfin-UI-Plugin. |
| [Threadfin](https://github.com/Threadfin/Threadfin) | Separater M3U/XMLTV-Proxy mit Quellenzusammenführung, Senderfilter und EPG-Mapping. | Alternative für die Aufbereitung vor Jellyfin. Persönliche Gruppen und deren Oberfläche bleiben Aufgabe dieses Plugins. |
| [TVHeadend](https://jellyfin.org/docs/general/server/plugins/tvheadend/) / [NextPVR](https://jellyfin.org/docs/general/server/plugins/) | Andere Live-TV-Backends mit Programmdaten. | Relevant bei entsprechendem bestehendem Backend; kein Anlass, wegen eines Gruppen-UI-Fehlers allein das Backend zu wechseln. |

Recherche zur Versionskompatibilität: Jellyfin 12.1 wurde am 15.09.2026 veröffentlicht. File Transformation und Plugin Pages haben zum Recherchezeitpunkt Release 3.0.1.0 mit getrennten Artefakten für Jellyfin 12.0.0 und 12.1.0. Home Screen Sections 3.0.2.0 enthält ebenfalls beide Artefakte. Dein Projekt und Release-ABI sind auf 12.0 festgelegt. 12.1 sollte separat gebaut und getestet werden, bevor Unterstützung zugesagt wird; die bloße Änderung des Manifest-ABI reicht nicht als Nachweis.

Quellen: [Jellyfin 12.1](https://github.com/jellyfin/jellyfin/releases/tag/v12.1), [Plugin Pages 3.0.1.0](https://github.com/IAmParadox27/jellyfin-plugin-pages/releases/tag/3.0.1.0), [File Transformation 3.0.1.0](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation/releases/tag/3.0.1.0), [Home Screen Sections 3.0.2.0](https://github.com/IAmParadox27/jellyfin-plugin-home-sections/releases/tag/3.0.2.0), [Dispatcharr/Jellyfin-Einrichtung](https://github.com/Dispatcharr/Dispatcharr-Docs/blob/main/docs/en/getting-started.md).

## Architekturentscheidung für den Web-Guide

| Möglichkeit | Aufwand/Risiko | Bewertung |
| --- | --- | --- |
| Originale Guide-Komponente auf der eigenen Gruppenseite wiederverwenden | Bewahrt Layout und viele Funktionen; eigener, lokal begrenzter Datenadapter erforderlich. | Nur geeignet, wenn ein Prototyp auf 12.0/12.1 vollständige Isolation vom originalen Live-TV nachweist. |
| Eigene Guide-Komponente im originalen Layout | Volle Kontrolle, zusätzliche Pflege bei Layoutänderungen. | Rückfallvariante mit derselben gewünschten Oberfläche. Die bisherige kleine Timeline allein genügt nicht. |
| Eigene Gruppen-Seite über Plugin Pages | Besserer Einstieg und persönliche Settings; zusätzliche optionale Abhängigkeit. | Mit der neuen Live-TV-Gruppenansicht kombinieren. |

Jellyfins moderne `GuideView` verwendet die interne `Guide`-Komponente. Es gibt in den untersuchten Schnittstellen keinen belegten öffentlichen Plugin-Hook, der ihr einfach Gruppen-IDs übergibt. Ein globales Überschreiben von `fetch` oder allen API-Client-Abfragen würde die Kopplung vergrößern. Eine Wiederverwendung sollte daher an einem konkreten 12.0/12.1-Prototyp ausschließlich auf der eigenen Gruppenseite beurteilt werden. Globale API-Überschreibungen, native Live-TV-Filter und Änderungen an der originalen Guide-Instanz sind ausgeschlossen. Programme werden nur für eigene `/LiveTvGroups/...`-Anfragen auf die Gruppensender begrenzt; Jellyfins ursprüngliche Routen behalten ihren Vertrag. Unabhängig vom Ergebnis bleibt die Layoutkopie des originalen Live-TV das Produktziel; nur die technische Umsetzung variiert.

## Umsetzungsplan mit Abnahmebedingungen

### Phase 0 — Originales Live-TV vollständig von der Gruppenfunktion trennen

Diese Vorstufe ist verbindlicher Bestandteil der ersten Veröffentlichung.

1. Die Registrierung des `GuideFilterMiddleware` über dessen Startupfilter entfernen. Keine Gruppenfilter auf `/LiveTv/Channels`, `/LiveTv/Programs` oder weiteren originalen Routen registrieren, unabhängig von Clientnamen oder alten Konfigurationswerten.
2. `EnableGuideFilter`, Client-Ausnahmeliste, Guidefilter-API und die Auswahlordner „Programmführer wählen“ aus der aktiven Funktion entfernen. Das Öffnen eines Gruppenordners darf keine Einstellung für das originale Live-TV schreiben.
3. Den Gruppenbutton und das Overlay aus der originalen Live-TV-Seite entfernen. Den eigenen Hauptnavigationseintrag „Live-TV Gruppen“ sowie eine eigene Route verwenden. Gruppenassets und eigene Ereignisbehandlung auf diesen Bereich begrenzen.
4. Datenmigration definieren: Gruppen und Senderreferenzen erhalten. `ActiveGuideGroupId` darf nach Migration keinen nativen Filter mehr aktivieren; falls als persönliche Standardgruppe übernommen, nur als separat validierte Präferenz der eigenen Gruppenseite.
5. README, Dashboard und Benutzertexte entsprechend umstellen. Alte aktive Bestandsfilter dürfen auch nach Update und Neustart nicht fortwirken.

Betroffene Bereiche: `Web/GuideFilterMiddleware.cs`, `Web/GuideFilter.cs`, `Api/GroupsController.cs`, `Channel/GroupsChannel.cs`, `Model/ChannelGroup.cs`, `Configuration/*`, Webintegration und Dokumentation.

Abnahme: Bei alten aktivierten Filterwerten, Gruppenwechsel, Ausblenden, Umbenennen und Löschen bleiben originale Senderliste, Programme, Sortierung und Pagination identisch zum Jellyfin-Verhalten ohne Gruppeneingriff. Das gilt für Web und native Clients. Jellyfins eigene Benutzerberechtigungen und Filter gelten weiterhin.

### Phase 1 — Datenweg nachweisen und originale Guide-Einbindung prototypisieren

Größe: mittel. Voraussetzung: Phase 0, bekannte Server-/Clientversion, Beispielgruppe und Zugriff auf einen Testserver mit importierten EPG-Daten.

1. Dieselbe Sendergruppe im normalen Guide und Gruppen-Endpunkt mit identischem Zeitfenster vergleichen. Antwortstatus, normalisierte Grenzen, aufgelöste Sender, Anzahl Programme pro Sender und ungültige Daten erfassen.
2. `GroupGuideService` aus dem Controller herauslösen. Explizites Antwortmodell für Senderzeilen, Programme und verständliche Diagnose verwenden; Zuordnung und Zeitwerte validieren. Autorisierung bleibt an Jellyfins Benutzer-/Senderberechtigungen gebunden.
3. Abbrechbare Ladeaufträge und eindeutige Auftragsnummern für Gruppen-, Sender- und EPG-Ansichten ergänzen. Fehler mit „Erneut versuchen“ darstellen.
4. „Jetzt“ als mitlaufenden Modus einführen; bewusst gewählten Zeitraum festhalten. Servergrenzen verwenden, Kalendertage korrekt berechnen, Zoom und Scroll-/Fokuszustand erhalten.
5. Nur sichtbare Sender beziehungsweise begrenzte Blöcke laden, falls Messungen bei großen Gruppen dies erfordern. Cache erst nach Messung ergänzen; Benutzer, Berechtigungen und Gruppenrevision berücksichtigen.

6. Den aktuellen Gruppenbereich als gemeinsamen Dienst für Programme, Guide und Sender auflösen; sichtbare Gruppen als persönliche Präferenz modellieren.
7. Den originalen Guide ausschließlich auf der eigenen Gruppenseite mit zwei Gruppensendern prototypisch einbinden. Lokalen Datenadapter, Theme, Details, Aufnahme und Lebenszyklus prüfen. Falls direkte Wiederverwendung globale Eingriffe erfordert, sie verwerfen und eine eigene Komponente nach der originalen Layoutreferenz festlegen.

Betroffene Dateien: `Api/GroupsController.cs`, neuer `Services/GroupGuideService.cs`, `Services/GuideWindow.cs`, `Web/client.js`, `Web/client.css` sowie EPG-Tests.

Abnahme: In einer Gruppe mit zwei bekannten Sendern erscheinen laufende und kommende Sendungen wie im nativen Guide. Überlappende Sendungen und Mitternacht stimmen. Schnelles Wechseln zwischen Gruppen, Tabs und Zeitfenstern zeigt stets den letzten Auftrag. Fehlende Daten und Ladefehler sind unterscheidbar. „Jetzt“ bleibt auch nach Ablauf des ursprünglichen Fensters aktuell; Wechsel der Sommer-/Winterzeit funktioniert.

### Phase 2 — Live-TV-Gruppen im originalen Layout und persönliche Settings

Größe: mittel bis groß. Phase 1 ist Voraussetzung. Diese Phase liefert die gewünschte eigene Ansicht; TV-App-Filterfunktionen entfallen.

1. Unter „Live-TV Gruppen“ eine eigenständige Route mit Programme, Programmführer und Sendern im Layout des Originals aufbauen. Die gewählte Gruppe begrenzt alle drei Views. Das fixe dunkle Overlay durch eine zur normalen Seitennavigation passende Ansicht ersetzen. Layout für moderne und unterstützte Legacy-Oberfläche an der Originalreferenz prüfen.
2. Sichtbare Gruppen, Standardgruppe, zuletzt verwendete View und Zoom persönlich speichern. Bei Bedarf „Alle sichtbaren Gruppen“ als deduplizierte Vereinigung ergänzen. Ausgeblendete Gruppen bleiben in der Verwaltung erreichbar; globale Integrationen bleiben im Dashboard.
3. Plugin Pages optional über dessen öffentliche Registrierung anbinden. Registrierung/Entfernung zum Plugin-Lebenszyklus passend umsetzen; der Einstieg führt ausschließlich zur eigenen Gruppenseite. Ohne Plugin Pages eine eigene Route über File Transformation anbieten.
4. Versionskennung für Webassets, erfolgreicher Clientstart, Tastaturbedienung, Theme-Unterstützung und Übersetzbarkeit prüfen.
5. Das Prototypergebnis aus Phase 1 umsetzen. Originale Sendungsdetails und Wiedergabe verwenden, Rückkehr zur Gruppe erhalten. Aufnahmen danach mit klarem Datenumfang ergänzen. Abend-/Datumsauswahl, Sortierung ohne Ziehen und verständliche Fehlerbehandlung in den Settings ergänzen.

Abnahme: Die Ansicht entspricht sichtbar dem Aufbau des originalen Live-TV. Ein Gruppenwechsel ändert innerhalb „Live-TV Gruppen“ Programme, EPG-Zeilen und Senderkarten gemeinsam. Das originale Live-TV bleibt unverändert. Sender anderer Gruppen erscheinen nicht. Sichtbare Gruppen lassen sich wählen, ohne Gruppen zu löschen. Gruppen sind ohne Suche nach versteckten Icons erreichbar. Oberfläche funktioniert mit Maus, Touch und Tastatur. Persönliche Einstellungen benötigen keinen Serverneustart. Fehlende optionale Plugins blockieren die Gruppen-EPG nicht.

### Phase 3 — Separate Gruppen-Zugänge in TV-Apps prüfen

Größe: abhängig von den tatsächlich unterstützten Appfunktionen. Diese Phase enthält keinen Filter des originalen Live-TV.

1. Gruppenordner und Playlist-Zugänge auf Android TV 0.19.10 und gegebenenfalls Wholphin prüfen. Sie bleiben eigenständige Zugänge neben dem originalen Live-TV.
2. Wiedergabe, Senderreihenfolge und Playlist-Abgleich prüfen; vorhandene Einschränkungen der Kanalobjekte dokumentieren.
3. Prüfen, ob eine Zielapp eigene Pluginseiten oder ein unabhängiges Gruppen-EPG unterstützt. Ohne entsprechenden Clientzugang keine vollständige Layoutkopie in dieser App zusagen. Eine notwendige Clienterweiterung gesondert planen.
4. Auf jedem Zielgerät nach Gruppenaktionen kontrollieren, dass der originale Guide alle erlaubten Sender gemäß Jellyfin weiter anzeigt. Kein Gruppenwechsel löst eine serverseitige Beschränkung oder ein Zurücksetzen des originalen Guides aus.

Abnahme: Separate Gruppenzugänge funktionieren entsprechend den nachgewiesenen Clientfähigkeiten. Originale Live-TV-Senderliste, Guide, Favoriten, Suche, Pagination und Zapping bleiben unabhängig von jeder Gruppenwahl.

### Phase 4 — Datenpflege, Wiedergabe und Betrieb

Größe: mittel bis groß; einzelne Teile können getrennt veröffentlicht werden.

1. Mehrdeutige Senderzuordnungen, Reparaturvorschau, stabile Quellkennungen und fehlende Sender sichtbar machen.
2. Versionsschema für gespeicherte Daten, Sicherungen, Revision/ETag und serverseitige Eingabevalidierung ergänzen.
3. Playlist-Abgleich mit Status und manueller Aktion verbessern; Hintergrundarbeit abbrechbar und gebündelt ausführen.
4. App-/Playlist-Wiedergabe mit nativer Live-TV-Wiedergabe vergleichen. Regelmäßiger Live-TV-Weg bleibt bevorzugt. Falls ein Serverproxy nötig wird, authentifizierte Stream-Endpunkte, Lebenszyklus und echte Verbindungslimits separat entwerfen und testen.
5. Optional Vorlagen, Import/Export und Home-Screen-Abschnitt ergänzen. Import aus M3U-`group-title` nur als separaten, vom Administrator konfigurierten Quellimport mit Vorschau planen; keine Provider-URLs an Browser ausgeben.
6. Build-/Testmatrix für Jellyfin 12.0 und 12.1; je unterstütztem ABI passendes Artefakt und Manifest. Test- und stabile Veröffentlichungen trennen.

Abnahme: Neuimport verliert keine Gruppen und wählt keinen mehrdeutigen Sender automatisch. Zwei parallele Bearbeitungen überschreiben sich nicht unbemerkt. Beschädigte Dateien bleiben wiederherstellbar. Playlist-/Wiedergabefehler sind nachvollziehbar; Limits und sensible Quellpfade sind im tatsächlichen Verhalten geprüft.

## Teststrategie und erste sinnvolle Veröffentlichung

Die vorhandenen Tests prüfen überwiegend Hilfslogik. Sie decken keinen vollständigen EPG-Abruf, Browser-Ladezyklus oder TV-App-Cache ab.

- C#-Integration: Guide-Endpunkt mit erlaubten/gesperrten Sendern, Programmdaten, Überlappungen und leerem Zeitraum; serialisierte Antwort mit gültiger Senderzuordnung prüfen.
- HTTP-Integration: Authentifizierung und Base-URL der eigenen Gruppenrouten. Originale GET-/POST-Live-TV-Routen behalten Inhalte, Filter, Sortierung, Pagination und Header auch mit alten aktivierten Gruppenfilterwerten. Keine Middleware verändert sie.
- Browser: verzögerte Antworten gezielt in anderer Reihenfolge liefern; Tabs/Gruppe/Zeitraum wechseln; Refresh, Fokus, kleine Displays, Mitternacht und Zeitumstellung prüfen.
- Geräte: verwendete Appversionen als konkrete Matrix führen. Wiedergabe, Aufnahme und Neuladen am echten Gerät prüfen; Hauptbranch-Lektüre ersetzt diese Prüfung nicht.
- Regression: Isolation der eigenen Gruppenroute vom originalen Live-TV, Migration aktivierter Bestandsfilter, Benutzertrennung und Berechtigungsänderungen, fehlende File Transformation/Plugin Pages, weitere Transformationen, Neuimport und Playlist-Abgleich.

Erste Veröffentlichung nach der präzisierten Anforderung: **Phase 0, Phase 1 und Phase 2 zusammen als eigenständige Live-TV-Gruppenansicht im originalen Layout, einschließlich integriertem EPG und Auswahl sichtbarer Gruppen. Das originale Live-TV bleibt in allen Clients unbeeinflusst.** Separate App-Zugänge und Datenpflege folgen getrennt. Die Versionsnummern werden erst für die tatsächlich fertiggestellten Änderungen vergeben.


## Umsetzungsstand — Dev 0.3.0.1

Die erste Veröffentlichung setzt die eigene Webansicht aus Phase 0–2 um: native Filter/Middleware und deren Auswahlfunktionen sind entfernt, der Gruppen-Guide hat einen eigenen Dienst und Datenbereich, Programme/Guide/Sender stehen in einer normalen Gruppenseite bereit. Gruppen und Playlist-IDs bleiben erhalten; persönliche Sichtbarkeit, Standard-/letzte Ansicht und Zoom sind separat gespeichert. Plugin Pages 3.x ist optional registriert; ohne dieses Plugin führt der bestehende Gruppenkanal zur gleichen Seite.

Die Guide-Komponente wurde nach der originalen Layoutreferenz eigenständig umgesetzt. Jellyfins interne React-Komponente bietet keinen nachgewiesenen öffentlichen lokalen Datenadapter; globale API-Overrides wurden deshalb nicht eingeführt. Die native Detailseite bleibt für Sendungsdetails, Wiedergabe und Aufnahme zuständig.

Abnahme: Backendtests prüfen Migration, Benutzertrennung, fehlende native Filterregistrierung und gültige überlappende EPG-Daten. Browserregressionen prüfen Details/Zurück mit Scrollzustand, Einstellungen, gemeinsame Gruppensender ohne Duplikate, Suche, verspätete Antworten, Fehler/Retry, mobile Beschriftungen, Datumsauswahl und Gruppenbearbeitung/Senderreihenfolge. Der Webprototyp wurde zusätzlich mit den vorhandenen Crime-EPG-Daten des Testservers betrachtet; hierfür wurden nur im Testbrowser Assets/API-Antworten adaptiert. Das neue C#-Plugin wurde nicht auf diesem Server installiert.

Offen bleiben reale Tests nach Installation des Dev-Builds, Aufnahme und Wiedergabe am Zielgerät, Plugin-Pages-Ladetest mit tatsächlich installiertem Drittplugin, Legacy-/weitere Theme-Varianten sowie Jellyfin 12.1. Phase 3 und Phase 4 bleiben separate Folgeschritte: native App-Zugänge, robuste Reparatur mehrdeutiger Sender, Revision/ETag, Backups, Diagnose/Playliststatus, optional Import/Export/Vorlagen und zusätzliche ABI-Artefakte. Die erste Version behauptet diese weiteren Funktionen nicht.
