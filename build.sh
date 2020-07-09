#!/bin/bash

dotnet tool restore
dotnet paket restore
dotnet build src/Aardvark.sln --configuration Release