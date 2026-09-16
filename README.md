# Live-TV Groups für Jellyfin

Jellyfin-Plugin, mit dem jeder Benutzer Live-TV-Sender zu eigenen Gruppen zusammenfassen kann, z. B. „Öffentlich-Rechtliche“, „Sport“ oder „Doku“.

- **Web-Client:** In Live-TV erscheint der Button **Gruppen**. Dort legst du Gruppen an, wählst Sender aus, sortierst per Drag & Drop und startest Sender direkt.
- **Apps** (Android TV, Mobile, …): Die Gruppen erscheinen unter **Kanäle → Live-TV Gruppen**.
- **Apps ohne Kanal-Unterstützung** (z. B. Wholphin): Optional erscheinen die Gruppen als Wiedergabelisten „Live-TV: Name“. Einschalten unter Dashboard → Plugins → Live-TV Groups.
- Gruppen sind **pro Benutzer**. Gesperrte Sender (Jugendschutz, Freigaben) bleiben unsichtbar.

Voraussetzung: **Jellyfin 12.0**.

## Installation

1. Dashboard → Plugins → Repositorys → **+** und diese URL eintragen:
   ```
   https://github.com/iEnki/jellyfin-plugin-livetv-groups/releases/latest/download/manifest.json
   ```
2. Für die Web-Integration zusätzlich das Repository von **File Transformation** hinzufügen:
   ```
   https://www.iamparadox.dev/jellyfin/plugins/manifest.json
   ```
3. Im Katalog **Live-TV Groups** und **File Transformation** installieren und Jellyfin neu starten.
4. Status prüfen: Dashboard → Plugins → Live-TV Groups.

## Hinweise

- Ändern sich die Sender-IDs, etwa weil die M3U neu erzeugt und neu eingelesen wurde, ordnet das Plugin die Sender über Name und Nummer neu zu.
- **App-Kanal:** Jellyfin kann Live-Streams über Kanäle nicht auf dem normalen Live-TV-Weg öffnen. Das Plugin reicht den Tuner-Stream deshalb direkt weiter, und der Server remuxt ihn. Die Begrenzung gleichzeitiger Tuner-Streams greift dabei **nicht**. Beachte das Verbindungslimit deines Anbieters.
- Die Web-Integration hängt an der Oberfläche von jellyfin-web. Nach größeren Jellyfin-Updates kann eine neue Plugin-Version nötig sein.

## Entwicklung

```bash
dotnet test
```

Release: Tag `vX.Y.Z` pushen. GitHub Actions baut das Plugin, erstellt das Release und trägt die Version in `manifest.json` ein.

Lizenz: GPL-3.0
