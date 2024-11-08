#!/bin/bash
pkill VetraHub

git pull origin main

dotnet build
dotnet run


# chmod +x update-server.sh
