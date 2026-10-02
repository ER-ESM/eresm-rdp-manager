# ERESM-RDP-Manager

Remotedesktop-Verbindungen verwalten und in einem Fenster nutzen – mit Tabs, Raster und Wachhalten.

## Funktionen

- **Gespeicherte Verbindungen** mit Gruppen und Suche; Server (auch mit Port), Benutzer, Domäne, RD-Gateway, Zwischenablage/Laufwerke/Drucker, Admin-Sitzung, Notizen
- **Passwörter verschlüsselt** (Windows DPAPI, an das Windows-Konto gebunden)
- **Sitzungen als Tabs** – verschiebbar, per Rechtsklick oder Ziehen als **eigenes Fenster**
- **Raster**: Knopf „Raster“ öffnet einen Dialog mit Aufteilungen (2 nebeneinander, 2×2, 3×3 …), offene Verbindungen per Drag & Drop in die Felder; zusätzlich Tabs direkt in den Sitzungsbereich ziehen (Ränder teilen, Mitte ersetzt)
- **Wachhalten**: drückt im eingestellten Abstand unsichtbar die Umschalt-Taste in der Sitzung (direkt über das RDP-Steuerelement, ohne Fokuswechsel)
- **Vollbild** auf den ganzen Monitor, Auflösung passt sich Fenster bzw. Rasterzelle an
- **Darstellung**: Wie Windows, Hell, Dunkel, ERESM Design
- **Sprache**: Deutsch / Englisch (Auswahl mit Flagge)
- **Import** von `.rdp`-Dateien, **Direktstart** `ERESM-RDP-Manager.exe <Verbindungsname> …`

## Bauen

Kein SDK nötig – der .NET-Framework-Compiler ist in jedem Windows enthalten:

```
build.cmd
```

Ergebnis: `ERESM-RDP-Manager.exe` (eine Datei, keine Installation). Das Programmsymbol wird beim Bauen aus dem Code gezeichnet (`app.ico`).

## Daten

`%APPDATA%\RdpManager\`

| Datei | Inhalt |
|---|---|
| `verbindungen.xml` | Verbindungen, Gruppen, Fenster, Darstellung, Sprache (Passwörter nur verschlüsselt) |
| `verbindungen.xml.bak` | vorheriger Stand, bei jedem Speichern |
| `protokoll.log` | Verbindungen, Trennungen mit Grund, Fehler – ohne Passwörter |

## Aufbau

| Datei | Aufgabe |
|---|---|
| `Rdp.cs` | Anbindung an das Windows-RDP-ActiveX (`mstscax.dll`), Ereignisse, `SendKeys` |
| `SessionPage.cs` | eine Sitzung: Verbinden, Status, Größe, Vollbild, Wachhalten |
| `MainForm.cs` | Hauptfenster, Verbindungsliste, Tabs, Raster, Ziehen |
| `Layout.cs` | Rasterzelle, Ablage-Anzeige |
| `GridDialog.cs` | Raster-Dialog |
| `TabStrip.cs` | eigene Tableiste (Verschieben, Herausziehen) |
| `SessionWindow.cs` | Sitzung im eigenen Fenster |
| `Theme.cs` | Darstellungen |
| `L.cs` | Übersetzungen, Flaggen |
| `Dialogs.cs` | Verbindung bearbeiten, Passwort |
| `Model.cs` | Speichern, Verschlüsselung, Protokoll |

## Wachhalter (Vorgänger)

`wachhalter/` – kleines Einzelwerkzeug, das Fenster des normalen Remotedesktop-Programms (mstsc) wachhält. Bauen mit `wachhalter\build.cmd`.
