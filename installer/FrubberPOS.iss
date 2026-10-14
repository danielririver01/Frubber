; Frubber POS 1.0 — Instalador Inno Setup (JHO-54, ADR-020 §16.8)
; Este script vive FUERA de la solución (no es un proyecto de FrubberPOS.sln).
; Compilar con: installer\build-installer.ps1 (requiere Inno Setup 6.3+,
; que es el que trae el asistente con estilo moderno).
; Los assets de assets\ se regeneran con installer\make-assets.ps1.

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

[Setup]
AppId={{7C9B2E4A-3F5D-4C8A-9E1B-6D2A8F0C5E71}
AppName=Frubber POS
AppVersion={#MyAppVersion}
AppPublisher=Frubber
AppComments=Punto de venta offline para fruver
DefaultDirName={autopf}\Frubber POS
DefaultGroupName=Frubber POS
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=output
OutputBaseFilename=FrubberPOS-Setup-{#MyAppVersion}
SetupIconFile=..\src\FrubberPOS.Desktop\Resources\frubber-pos.ico
; Contrato de licencia (Licencia+RTF): hasta «Acepto» no se puede avanzar;
; «No acepto» solo permite Cancelar y el instalador se cierra sin tocar nada.
LicenseFile=assets\Licencia-FrubberPOS.rtf
; Estilo de asistente moderno + bitmaps de marca (generados por make-assets.ps1).
WizardStyle=modern
WizardImageFile=assets\wizard-164.bmp
WizardSmallImageFile=assets\wizard-55.bmp
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayIcon={app}\FrubberPOS.Desktop.exe

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
; Publicación autocontenida x64 generada por build-installer.ps1.
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Frubber POS"; Filename: "{app}\FrubberPOS.Desktop.exe"
Name: "{group}\Desinstalar Frubber POS"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Frubber POS"; Filename: "{app}\FrubberPOS.Desktop.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"

[Run]
Filename: "{app}\FrubberPOS.Desktop.exe"; Description: "Abrir Frubber POS ahora"; Flags: postinstall nowait skipifsilent

[Code]
// JHO-54: desinstalar NUNCA borra los datos del negocio sin advertencia.
// La base (ventas, productos, licencia, registros) vive en C:\ProgramData\FrubberPOS.
function InitializeUninstall(): Boolean;
begin
  if MsgBox('¿Desinstalar Frubber POS?' + #13#10#13#10 +
            'Los datos del negocio (C:\ProgramData\FrubberPOS) se conservan siempre. ' +
            'Para borrarlos, elimine esa carpeta manualmente después.', mbConfirmation, MB_YESNO) = IDYES then
    Result := True
  else
    Result := False;
end;
