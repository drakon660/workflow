@echo off
echo Cleaning build artifacts...
for /d /r %%d in (bin,obj) do @if exist "%%d" (echo Deleting: %%d & rd /s /q "%%d")
echo Clean complete!
