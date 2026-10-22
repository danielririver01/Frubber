;=====================================================================
; Frubber POS 2.0.0
; Archivo: installer\backup-frubberpos-scheduled.iss   (UNCHANGED NAME)
; M2-30: instalador de la TAREA PROGRAMADA diaria de pg_dump.
;
; REESCRITO el 2026-10-22: la version anterior declaraba claves
; (TaskName=...) que NO son sintaxis de Inno Setup y llamaba a un
; make-task-scheduled.vbs inexistente. Este archivo ahora ES un guion
; Inno Setup real ([Setup]/[Files]/[Run]) que deja la tarea con
; schtasks.exe, corre como SYSTEM (funciona aunque nadie haya iniciado
; sesion) y exactamente una vez al dia a las 02:00.
;
; Requisitos previos (los valida el [Code]):
;   - scripts\backup-frubberpos.ps1 debe estar instalado en el PC
;     del servidor (lo copia este instalador a {autopf}\scripts).
;   - Los puestos no se conectan por este instalador; es SOLO para el
;     servidor del local (M2-32 lo integrara al instalador del server).
;
; Compilacion (fuera de FrubberPOS.sln, ADR-020 16.8):
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\backup-frubberpos-scheduled.iss
;=====================================================================

[Setup]
AppName=Frubber POS - Tarea de respaldo diario
AppVersion=2.0.0
AppId={{7C3B4A1F-8E06-4D2A-B42F-5FD8D4A39C77}
DefaultDirName={autopf}\FrubberPOS
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=FrubberPOS-TareaRespaldo-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Uninstallable=yes

[Files]
Source: "..\scripts\backup-frubberpos.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

// La tarea corre como SYSTEM para que no dependa del login del cajero.
procedure CurStepChanged(CurStep: TSetupStep);
var
  Params, Cmd, WorkingDir: string;
  R: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    WorkingDir := ExpandConstant('{app}\scripts');
    Params := Format('-NoProfile -ExecutionPolicy Bypass -File "%s\backup-frubberpos.ps1" -BackupDir "C:\Backups\FrubberPOS" -RetentionDays 30',
                     [WorkingDir]);

    // Borra la tarea previa si existe (idempotencia al reinstalar)
    Exec('schtasks.exe', '/Delete /TN FrubberPosBackupDaily /F', '', SW_HIDE, ewWaitUntilTerminated, R);

    Cmd := ExpandConstant(Format('/Create /F /SC DAILY /ST 02:00 /RL HIGHEST /TN FrubberPosBackupDaily ' +
        '/TR "powershell.exe %s"', [Params]));
    Exec('schtasks.exe', Cmd, WorkingDir, SW_HIDE, ewWaitUntilTerminated, R);
    if R <> 0 then
      MsgBox('No se pudo registrar la tarea programada (codigo ' + IntToStr(R) + ').', mbError, MB_OK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var R: Integer;
begin
  if CurUninstallStep = usPostUninstall then
    Exec('schtasks.exe', '/Delete /TN FrubberPosBackupDaily /F', '', SW_HIDE, ewWaitUntilTerminated, R);
end;
