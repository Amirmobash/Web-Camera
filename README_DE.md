# Gabelstapler Camera Monitor

A simple Windows desktop application for showing a live USB camera feed from a forklift.

This project was built for a practical use case: a webcam is mounted near the forklift fork, so the driver can see the fork position more clearly while working. The interface is designed in German and uses an orange industrial-style theme to match the forklift environment.

## What it does

The application opens a USB camera or webcam and shows the live video inside a Windows Forms interface.

It also adds a red horizontal guide line over the camera image. This line can be moved up and down by the user and is meant to mark the approximate fork tip position.

The driver can use this line as a visual reference while positioning the forks.

## Main features

- Live USB camera preview
- German user interface
- Orange and black forklift-style design
- Red adjustable fork-tip guide line
- Mouse wheel support for moving the guide line
- Buttons for moving the guide line up and down
- Start, stop, and refresh camera controls
- Optional video recording
- Camera locking through `App.config`
- Simple Windows Forms project structure

## Use case

This software is intended for forklifts where a camera is mounted near the fork area.

The driver can see the fork position on a screen and use the red guide line as a reference point for the fork tip. This can make positioning easier, especially when visibility is limited.

## Requirements

- Windows
- Visual Studio
- .NET Framework 4.8
- A USB camera or webcam
- A connected display inside or near the forklift cabin

## How to build

Open the solution file in Visual Studio:

```text
GabelstaplerKameraMonitor.sln
