# Gabelstapler Kamera-Monitor

Windows-Forms-Anwendung fuer eine USB- oder Webkamera am Gabelstapler.

## Funktionen

- Deutsche Bedienoberflaeche
- Orange/schwarzes Stapler-Design
- Livebild der Gabelkamera
- Rote horizontale Gabelspitzen-Linie im Kamerabild
- Linie per Mausrad im Bild nach oben oder unten verschiebbar
- Zusatzbuttons fuer Linie hoch/runter
- Aufnahmefunktion als AVI-Datei
- Kamera kann ueber App.config festgelegt werden

## Bedienung

- Start: Kamera starten
- Stop: Kamera stoppen
- Aktualisieren: Kamera neu laden
- Aufnahme starten: Livebild inklusive roter Linie aufnehmen
- Aufnahme stoppen: AVI-Datei speichern
- Mausrad im Kamerabild: rote Gabelspitzen-Linie verschieben

## Einstellungen

Die Einstellungen liegen in App.config.

- TargetCameraName: Name der gewuenschten Kamera
- CameraMatchMode: Contains oder Exact
- RecordingFolder: Ordner fuer Aufnahmen
- RecordingFps: Bilder pro Sekunde fuer die Aufnahme
- RecordingQuality: JPEG-Qualitaet fuer AVI-Aufnahme
- InitialForkLinePercent: Startposition der roten Linie in Prozent
- ForkLineStepPixels: Schrittweite beim Verschieben der Linie

Wenn TargetCameraName leer ist, wird die erste gefundene Kamera verwendet.

## Build

build.bat starten oder die Solution in Visual Studio oeffnen.

Zielplattform: .NET Framework 4.8
