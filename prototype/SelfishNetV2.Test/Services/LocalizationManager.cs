// FREE MODEL TEST PROTOTYPE — LocalizationManager (hot-swappable EN/es-419).
// Spec: docs/LOCALIZATION_SPEC.md. No restart, no thread interruption on switch.

namespace SelfishNetV2.Test.Services;

public static class LocalizationManager
{
    public enum Language { English, SpanishLA }

    public static Language CurrentLanguage { get; private set; } = Language.English;

    public static event Action? OnLanguageChanged;

    public static void SetLanguage(Language lang)
    {
        if (CurrentLanguage == lang) return;
        CurrentLanguage = lang;
        OnLanguageChanged?.Invoke();
    }

    private static readonly Dictionary<string, (string En, string Es)> Strings = new()
    {
        ["AppTitle"] = ("SelfishNet v2 - Network Traffic Controller",
                        "SelfishNet v2 - Controlador de Tráfico de Red"),
        ["MenuFile"] = ("File", "Archivo"),
        ["MenuView"] = ("View", "Ver"),
        ["MenuSettings"] = ("Settings", "Configuración"),
        ["MenuLanguage"] = ("Language", "Idioma"),
        ["MenuHelp"] = ("Help", "Ayuda"),
        ["BtnDiscover"] = ("Discover Devices", "Descubrir Dispositivos"),
        ["BtnDiscoverTooltip"] = ("Scan local network for connected devices (ARP scan)",
                                  "Escanear la red local en busca de dispositivos conectados (escaneo ARP)"),
        ["BtnStart"] = ("Start Redirecting-Spoofing", "Iniciar Redirección-Spoofing"),
        ["BtnStartTooltip"] = ("Begin ARP spoofing and bandwidth throttling on selected devices",
                               "Iniciar spoofing ARP y control de ancho de banda en dispositivos seleccionados"),
        ["BtnStop"] = ("Stop", "Detener"),
        ["BtnStopTooltip"] = ("Stop redirecting and safely restore original network ARP tables",
                              "Detener redirección y restaurar de forma segura las tablas ARP originales"),
        ["BtnSpoofStart"] = ("Start Spoofing", "Iniciar Spoofing"),
        ["BtnSpoofStop"] = ("Stop Spoofing", "Detener Spoofing"),
        ["BtnAdapter"] = ("Network Adapter", "Adaptador de Red"),
        ["BtnAdapterTooltip"] = ("Select active network interface and gateway",
                                 "Seleccionar la interfaz de red activa y puerta de enlace"),
        ["BtnAbout"] = ("About / Help", "Acerca de / Ayuda"),
        ["ColPCName"] = ("PC Name", "Nombre del Equipo"),
        ["ColPCIP"] = ("IP Address", "Dirección IP"),
        ["ColPCMac"] = ("MAC Address", "Dirección MAC"),
        ["ColDownload"] = ("Download (KB/s)", "Descarga (KB/s)"),
        ["ColUpload"] = ("Upload (KB/s)", "Subida (KB/s)"),
        ["ColDownCap"] = ("Download Cap (KB/s)", "Límite Descarga (KB/s)"),
        ["ColUploadCap"] = ("Upload Cap (KB/s)", "Límite Subida (KB/s)"),
        ["ColBlock"] = ("Block", "Bloquear"),
        ["ColSpoof"] = ("Spoof", "Spoofear (Interceptar)"),
        ["Unlimited"] = ("0 (Unlimited)", "0 (Ilimitado)"),
        ["LocalHost"] = ("Your PC (Local)", "Tu Equipo (Local)"),
        ["Gateway"] = ("Default Gateway (Router)", "Puerta de Enlace (Router)"),
        ["StatusReady"] = ("Ready", "Listo"),
        ["StatusScanning"] = ("Scanning local network...", "Escaneando la red local..."),
        ["StatusRedirecting"] = ("Intercepting & shaping traffic ({0} devices targeted)",
                                 "Interceptando y controlando tráfico ({0} dispositivos objetivo)"),
        ["StatusStopped"] = ("Stopped. Restoring ARP tables...", "Detenido. Restaurando tablas ARP..."),
        ["AdapterDialogTitle"] = ("Select Network Adapter", "Seleccionar Adaptador de Red"),
        ["AdapterSelectPrompt"] = ("Please select the network adapter connected to your LAN:",
                                   "Selecciona el adaptador de red conectado a tu red local:"),
        ["AdapterIP"] = ("Local IP: {0}", "IP Local: {0}"),
        ["AdapterGateway"] = ("Gateway IP: {0}", "Puerta de Enlace: {0}"),
        ["AdapterForwardingOn"] = ("Windows packet forwarding is active.", "El reenvío de paquetes de Windows está activo."),
        ["AdapterForwardingOff"] = ("Windows packet forwarding is inactive (Internal forwarder enabled).",
                                    "El reenvío de Windows no está activo (Reenvío interno activado)."),
        ["BtnOK"] = ("OK", "Aceptar"),
        ["BtnCancel"] = ("Cancel", "Cancelar"),
        ["MsgNoAdapter"] = ("No network card with an active gateway has been found!",
                            "¡No se encontró ninguna tarjeta de red con una puerta de enlace activa!"),
        ["MsgDriverNotFound"] = ("Packet capture driver not detected. Please install Npcap or WinPcap.",
                                 "No se detectó el controlador de captura de paquetes. Por favor instala Npcap o WinPcap."),
        ["MsgAdminRequired"] = ("Administrator privileges are required to send raw network packets.",
                                "Se requieren permisos de Administrador para enviar paquetes de red directos."),
        ["MsgConfirmQuit"] = ("Are you sure you want to quit? Active traffic control will be stopped and ARP tables restored.",
                              "¿Seguro que deseas salir? El control de tráfico activo se detendrá y se restaurarán las tablas ARP."),
        ["MsgRestoringArp"] = ("Restoring network cache, please wait...", "Restaurando caché de red, por favor espera..."),
        ["TotalTraffic"] = ("Total: Down: {0:F1} KB/s | Up: {1:F1} KB/s", "Total: Bajada: {0:F1} KB/s | Subida: {1:F1} KB/s"),
        ["DevicesCount"] = ("Devices: {0}", "Dispositivos: {0}"),
        ["CapDownName"] = ("Download Cap", "Límite de Descarga"),
        ["CapUpName"] = ("Upload Cap", "Límite de Subida"),
        ["SpeedDownName"] = ("Download", "Descarga"),
        ["SpeedUpName"] = ("Upload", "Subida"),
        ["BtnUnits"] = ("Units: {0}", "Unidades: {0}"),
        ["BtnUnitsTooltip"] = ("Toggle speed/limit units between kilobytes and kilobits (KByte/s ⇄ KBit/s)",
                               "Cambiar las unidades de velocidad/límite entre kilobytes y kilobits (KByte/s ⇄ KBit/s)"),
        ["NameColumnTooltip"] = ("Double-click a name to rename it. Names are remembered by MAC address.",
                                 "Doble clic en un nombre para cambiarlo. Los nombres se recuerdan por dirección MAC."),
        ["MsgForwardingOn"] = ("Windows packet forwarding is enabled. The system will forward traffic around this app, so limits may NOT apply. Disable 'IPEnableRouter' for full control. Continue anyway?",
                                "El reenvío de paquetes de Windows está activado. El sistema reenviará el tráfico sin pasar por esta app, así que los límites podrían NO aplicarse. Desactiva 'IPEnableRouter' para un control total. ¿Continuar de todos modos?"),
        ["MsgGatewayUnknown"] = ("Gateway MAC address is still unknown. Limits and forwarding may not work until the gateway answers. Run Discover first, then Start. Continue anyway?",
                                  "Aún se desconoce la dirección MAC de la puerta de enlace. Los límites y el reenvío podrían no funcionar hasta que responda. Ejecuta Descubrir primero y luego Iniciar. ¿Continuar de todos modos?"),
        ["AboutText"] = ("SelfishNet v2\nLAN traffic controller: ARP discovery, traffic shaping and device blocking.\nUse only on networks you own or are authorized to test.",
                         "SelfishNet v2\nControlador de tráfico LAN: descubrimiento ARP, control de ancho de banda y bloqueo de dispositivos.\nÚsalo solo en redes propias o autorizadas."),
    };

    public static string Get(string key, params object[] args)
    {
        if (!Strings.TryGetValue(key, out var pair))
            return key;
        string raw = CurrentLanguage == Language.English ? pair.En : pair.Es;
        return args.Length == 0 ? raw : string.Format(raw, args);
    }
}
