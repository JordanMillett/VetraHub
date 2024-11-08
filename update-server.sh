#!/bin/bash
pkill VetraHub

git pull origin main

dotnet build
dotnet run

