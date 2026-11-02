# Localization Architecture & Bilingual Dictionary
## English (US) & Latin American Spanish (Español Latinoamericano)

> **Historical spec; the code is the current source of truth.** The live key/value
> table is `src/SelfishNetV2.Services/LocalizationManager.cs` (default UI language:
> Spanish LA). Some keys in the table below are historical: the menu keys and the
> removed Spoof column are no longer used, and several button labels were shortened
> during the rebuild. Update `LocalizationManager` and both guides together when a
> user-visible string changes.

This specification defines the dynamic localization subsystem for the modernized SelfishNet. The language can be toggled instantly from the UI (e.g., via a language selector dropdown, toolbar button, or hotkey) without restarting the application, interrupting background network captures, or dropping ongoing ARP spoofing/redirection sessions.

---

## 1. Architecture: Hot-Swappable Localization Manager

```csharp
public static class LocalizationManager
{
    public enum Language { English, SpanishLA }
    public static Language CurrentLanguage { get; private set; } = Language.English;
    
    public static event Action OnLanguageChanged;

    public static void SetLanguage(Language lang)
    {
        if (CurrentLanguage == lang) return;
        CurrentLanguage = lang;
        OnLanguageChanged?.Invoke();
    }

    public static string Get(string key) => ...;
}
```

Every UI component registers an update hook with `LocalizationManager.OnLanguageChanged`. When toggled:
- No network sockets or worker threads are touched.
- DataGridView column headers, tooltips, status labels, and dialog prompts are updated immediately in-place.

---

## 2. Bilingual String Table

| Key | English (US) | Latin American Spanish (es-419) | Context |
| :--- | :--- | :--- | :--- |
| `AppTitle` | SelfishNet v2 - Network Traffic Controller | SelfishNet v2 - Controlador de Tráfico de Red | Main Window Title |
| `MenuFile` | File | Archivo | Main Menu |
| `MenuView` | View | Ver | Main Menu |
| `MenuSettings` | Settings | Configuración | Main Menu |
| `MenuLanguage` | Language | Idioma | Language switch menu |
| `MenuHelp` | Help | Ayuda | Main Menu |
| `BtnDiscover` | Discover Devices | Descubrir Dispositivos | Toolbar Button 1 |
| `BtnDiscoverTooltip` | Scan local network for connected devices (ARP scan) | Escanear la red local en busca de dispositivos conectados (escaneo ARP) | Tooltip |
| `BtnStart` | Start Redirecting-Spoofing | Iniciar Redirección-Spoofing | Toolbar Button 2 |
| `BtnStartTooltip` | Begin ARP spoofing and bandwidth throttling on selected devices | Iniciar spoofing ARP y control de ancho de banda en dispositivos seleccionados | Tooltip |
| `BtnStop` | Stop | Detener | Toolbar Button 3 |
| `BtnStopTooltip` | Stop redirecting and safely restore original network ARP tables | Detener redirección y restaurar de forma segura las tablas ARP originales | Tooltip |
| `BtnAdapter` | Network Adapter | Adaptador de Red | Toolbar Button 4 |
| `BtnAdapterTooltip` | Select active network interface and gateway | Seleccionar la interfaz de red activa y puerta de enlace | Tooltip |
| `BtnAbout` | About / Help | Acerca de / Ayuda | Toolbar Button 5 |
| `ColPCName` | PC Name | Nombre del Equipo | DataGrid Column 0 |
| `ColPCIP` | IP Address | Dirección IP | DataGrid Column 1 |
| `ColPCMac` | MAC Address | Dirección MAC | DataGrid Column 2 |
| `ColDownload` | Download (KB/s) | Descarga (KB/s) | DataGrid Column 3 |
| `ColUpload` | Upload (KB/s) | Subida (KB/s) | DataGrid Column 4 |
| `ColDownCap` | Download Cap (KB/s) | Límite Descarga (KB/s) | DataGrid Column 5 |
| `ColUploadCap` | Upload Cap (KB/s) | Límite Subida (KB/s) | DataGrid Column 6 |
| `ColBlock` | Block | Bloquear | DataGrid Column 7 |
| `ColSpoof` | Spoof | Spoofear (Interceptar) | DataGrid Column 8 |
| `Unlimited` | 0 (Unlimited) | 0 (Ilimitado) | Numeric spinner label |
| `LocalHost` | Your PC (Local) | Tu Equipo (Local) | Device label |
| `Gateway` | Default Gateway (Router) | Puerta de Enlace (Router) | Device label |
| `StatusReady` | Ready | Listo | Status bar |
| `StatusScanning` | Scanning local network... | Escaneando la red local... | Status bar |
| `StatusRedirecting` | Intercepting & shaping traffic ({0} devices targeted) | Interceptando y controlando tráfico ({0} dispositivos objetivo) | Status bar |
| `StatusStopped` | Stopped. Restoring ARP tables... | Detenido. Restaurando tablas ARP... | Status bar |
| `AdapterDialogTitle` | Select Network Adapter | Seleccionar Adaptador de Red | Adapter Form Title |
| `AdapterSelectPrompt` | Please select the network adapter connected to your LAN: | Selecciona el adaptador de red conectado a tu red local: | Adapter Form Label |
| `AdapterIP` | Local IP: {0} | IP Local: {0} | Adapter details |
| `AdapterGateway` | Gateway IP: {0} | Puerta de Enlace: {0} | Adapter details |
| `AdapterForwardingOn` | Windows packet forwarding is active. | El reenvío de paquetes de Windows está activo. | Adapter details |
| `AdapterForwardingOff` | Windows packet forwarding is inactive (Internal forwarder enabled). | El reenvío de Windows no está activo (Reenvío interno activado). | Adapter details |
| `BtnOK` | OK | Aceptar | Dialog button |
| `BtnCancel` | Cancel | Cancelar | Dialog button |
| `MsgNoAdapter` | No network card with an active gateway has been found! | ¡No se encontró ninguna tarjeta de red con una puerta de enlace activa! | Error message |
| `MsgDriverNotFound` | Packet capture driver not detected. Please install Npcap or WinPcap. | No se detectó el controlador de captura de paquetes. Por favor instala Npcap o WinPcap. | Error message |
| `MsgAdminRequired` | Administrator privileges are required to send raw network packets. | Se requieren permisos de Administrador para enviar paquetes de red directos. | Error message |
| `MsgConfirmQuit` | Are you sure you want to quit? Active traffic control will be stopped and ARP tables restored. | ¿Seguro que deseas salir? El control de tráfico activo se detendrá y se restaurarán las tablas ARP. | Confirm dialog |
| `MsgRestoringArp` | Restoring network cache, please wait... | Restaurando caché de red, por favor espera... | Notification |
| `TotalTraffic` | Total: Down: {0:F1} KB/s | Up: {1:F1} KB/s | Total: Bajada: {0:F1} KB/s | Subida: {1:F1} KB/s | Summary label |
| `DevicesCount` | Devices: {0} | Dispositivos: {0} | Summary label |
