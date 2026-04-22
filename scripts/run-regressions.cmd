@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0run-regressions.ps1" %*
