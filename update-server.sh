#!/bin/bash

# chmod +x /path/to/update-server.sh

# Navigate to the application directory
cd /path/to/your/project

# Stop the server (if applicable)
echo "Stopping server..."
# This could be a specific way to stop the server, for example:
# pkill -f 'dotnet yourapp.dll'

# Alternatively, if you're running it with a process manager like systemd:
# systemctl stop your-app.service

# Pull the latest changes from the Git repository
echo "Pulling latest changes from git..."
git pull origin main  # Adjust 'main' to the branch you're using

# Rebuild and run the application
echo "Starting server again..."
dotnet build
dotnet run &  # Running in the background

echo "Server updated and restarted!"
