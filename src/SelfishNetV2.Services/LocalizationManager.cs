namespace SelfishNetV2.Services;

/// <summary>
/// Hot-swappable bilingual strings (en-US / es-419). Switching language touches no
/// sockets or worker threads; UI controls subscribe to <see cref="OnLanguageChanged"/>
/// and re-read their keys in place. String matrix: docs/LOCALIZATION_SPEC.md + extras.
/// </summary>
public static class LocalizationManager
{
    public enum Language { English, SpanishLA }

    public static Language CurrentLanguage { get; private set; } = Language.SpanishLA;

    public static event Action? OnLanguageChanged;

    public static void SetLanguage(Language lang)
    {
        if (CurrentLanguage == lang) return;
        CurrentLanguage = lang;
        OnLanguageChanged?.Invoke();
    }

    private static readonly Dictionary<string, (string En, string Es)> Strings = new()
    {
        ["AppTitle"] = ("SelfishNet v2 — Network Traffic Controller",
                        "SelfishNet v2 — Controlador de Tráfico de Red"),
        ["MenuLanguage"] = ("Language", "Idioma"),
        ["BtnDiscover"] = ("Discover", "Descubrir"),
        ["BtnDiscoverTooltip"] = ("Scan local network for connected devices (ARP scan)",
                                  "Escanear la red local en busca de dispositivos conectados (escaneo ARP)"),
        ["BtnStart"] = ("Start Shaping", "Iniciar Control"),
        ["BtnStartTooltip"] = ("Begin ARP spoofing and bandwidth throttling on selected devices",
                               "Iniciar spoofing ARP y control de ancho de banda en dispositivos seleccionados"),
        ["BtnStop"] = ("Stop", "Detener"),
        ["BtnStopTooltip"] = ("Stop redirecting and safely restore original network ARP tables",
                              "Detener redirección y restaurar de forma segura las tablas ARP originales"),
        ["BtnAdapter"] = ("Adapter", "Adaptador"),
        ["BtnAdapterTooltip"] = ("Select active network interface and gateway",
                                 "Seleccionar la interfaz de red activa y puerta de enlace"),
        ["BtnResetLimits"] = ("Reset limits", "Restablecer límites"),
        ["BtnResetLimitsTooltip"] = ("Set every device's download and upload limit back to 0 (unlimited)",
                                     "Poner en 0 (ilimitado) los límites de descarga y subida de todos los dispositivos"),
        ["BtnAbout"] = ("About", "Acerca de"),
        ["BtnUnits"] = ("Units: {0}", "Unidades: {0}"),
        ["BtnUnitsTooltip"] = ("Toggle speed/limit units between kilobytes and kilobits (KByte/s ⇄ KBit/s)",
                               "Cambiar las unidades de velocidad/límite entre kilobytes y kilobits (KByte/s ⇄ KBit/s)"),
        ["BtnThemeTooltip"] = ("Switch between dark and light theme",
                               "Cambiar entre tema oscuro y claro"),
        ["ThemeDark"] = ("Dark", "Oscuro"),
        ["ThemeLight"] = ("Light", "Claro"),
        ["AboutEnv"] = ("\n\nSystem: {0}\nRuntime: {1}\nEngine: {2}",
                        "\n\nSistema: {0}\nEntorno: {1}\nMotor: {2}"),
        ["BtnLanguageTooltip"] = ("Switch between English and Spanish (applies instantly, no restart)",
                                  "Cambiar entre inglés y español (se aplica al instante, sin reiniciar)"),
        ["EngineLegacy"] = ("Legacy", "Legacy"),
        ["EngineModern"] = ("Modern", "Moderno"),
        ["EngineSwitchTooltip"] = ("Switch the packet engine at runtime — compare behavior side-by-side or fall back instantly if one misbehaves on your network",
                                   "Cambiar el motor de paquetes en ejecución — compara comportamientos o vuelve al clásico al instante si el moderno falla en tu red"),
        ["EngineGroupLabel"] = ("Engine:", "Motor:"),
        ["ColPCName"] = ("PC Name", "Nombre del Equipo"),
        ["ColPCIP"] = ("IP Address", "Dirección IP"),
        ["ColPCMac"] = ("MAC Address", "Dirección MAC"),
        ["ColDownload"] = ("Download", "Descarga"),
        ["ColUpload"] = ("Upload", "Subida"),
        ["ColDownCap"] = ("Down Cap", "Límite Descarga"),
        ["ColUpCap"] = ("Up Cap", "Límite Subida"),
        ["ColBlock"] = ("Block", "Bloquear"),
        ["ColSpoof"] = ("Spoof", "Interceptar"),
        ["CapUnitHeader"] = ("({0})", "({0})"),
        ["Unlimited"] = ("0 (Unlimited)", "0 (Ilimitado)"),
        ["LocalHost"] = ("Your PC (Local)", "Tu Equipo (Local)"),
        ["Gateway"] = ("Default Gateway (Router)", "Puerta de Enlace (Router)"),
        ["StatusReady"] = ("Ready", "Listo"),
        ["StatusScanning"] = ("Scanning local network...", "Escaneando la red local..."),
        ["StatusRedirecting"] = ("Intercepting & shaping traffic ({0} devices targeted)",
                                 "Interceptando y controlando tráfico ({0} dispositivos objetivo)"),
        ["StatusStopped"] = ("Stopped. Restoring ARP tables...", "Detenido. Restaurando tablas ARP..."),
        ["StatusSwitching"] = ("Switching engine ({0})...", "Cambiando de motor ({0})..."),
        ["StatusNoDriver"] = ("Driver not available — install Npcap to enable engines",
                              "Controlador no disponible — instala Npcap para habilitar los motores"),
        ["StatusDriverRestricted"] = ("Driver restricted to administrators — restart elevated to control traffic",
                                      "Controlador restringido a administradores — reinicia como administrador para controlar el tráfico"),
        ["MsgElevationRequired"] = ("The packet driver only allows administrators. Restart SelfishNet as administrator now?\n\nCancel keeps the app open, but capturing and shaping will not work.\n\nTip: Npcap can be installed with unrestricted access, which lets this app run without elevation.",
                                    "El controlador de paquetes solo permite administradores. ¿Reiniciar SelfishNet como administrador ahora?\n\nCancelar deja la app abierta, pero la captura y el control no funcionarán.\n\nConsejo: Npcap puede instalarse con acceso sin restricciones, lo que permite que esta app funcione sin elevación."),
        ["StatusNoAdapter"] = ("Select a network adapter to begin",
                               "Selecciona un adaptador de red para comenzar"),
        ["StatusRestored"] = ("ARP tables restored", "Tablas ARP restauradas"),
        ["StatusResolvingGateway"] = ("Resolving gateway MAC...", "Resolviendo MAC de la puerta de enlace..."),
        ["AdapterDialogTitle"] = ("Select Network Adapter", "Seleccionar Adaptador de Red"),
        ["AdapterSelectPrompt"] = ("Choose the network adapter connected to your LAN:",
                                   "Elige el adaptador de red conectado a tu red local:"),
        ["AdapterColName"] = ("Adapter", "Adaptador"),
        ["AdapterColIP"] = ("IP Address", "Dirección IP"),
        ["AdapterColGateway"] = ("Gateway", "Puerta de Enlace"),
        ["AdapterColForwarding"] = ("OS Forwarding", "Reenvío del SO"),
        ["AdapterForwardingOn"] = ("Active", "Activo"),
        ["AdapterForwardingOff"] = ("Inactive", "Inactivo"),
        ["BtnOK"] = ("OK", "Aceptar"),
        ["BtnCancel"] = ("Cancel", "Cancelar"),
        ["MsgNoAdapter"] = ("No network card with an active gateway has been found!",
                            "¡No se encontró ninguna tarjeta de red con una puerta de enlace activa!"),
        ["MsgDriverNotFound"] = ("Packet capture driver not detected. Npcap is required to control traffic — install it, then restart the app.",
                                 "No se detectó el controlador de captura de paquetes. Npcap es necesario para controlar el tráfico — instálalo y reinicia la app."),
        ["BtnDownloadNpcap"] = ("Download Npcap", "Descargar Npcap"),
        ["BtnDownloadNpcapTooltip"] = ("Open the official Npcap download page (npcap.com) in your browser",
                                       "Abrir la página oficial de descarga de Npcap (npcap.com) en tu navegador"),
        ["MsgAdminRequired"] = ("Administrator privileges are required to send raw network packets.",
                                "Se requieren permisos de Administrador para enviar paquetes de red directos."),
        ["MsgConfirmQuit"] = ("Are you sure you want to quit? Active traffic control will be stopped and ARP tables restored.",
                              "¿Seguro que deseas salir? El control de tráfico activo se detendrá y se restaurarán las tablas ARP."),
        ["MsgRestoringArp"] = ("Restoring network cache, please wait...", "Restaurando caché de red, por favor espera..."),
        ["MsgForwardingOn"] = ("Windows packet forwarding is enabled. The system will forward traffic around this app, so limits may NOT apply. Disable 'IPEnableRouter' for full control. Continue anyway?",
                               "El reenvío de paquetes de Windows está activado. El sistema reenviará el tráfico sin pasar por esta app, así que los límites podrían NO aplicarse. Desactiva 'IPEnableRouter' para un control total. ¿Continuar de todos modos?"),
        ["MsgGatewayUnknown"] = ("The gateway MAC address could not be resolved. Run Discover first and make sure the gateway answers ARP. Shaping was not started.",
                                 "No se pudo resolver la dirección MAC de la puerta de enlace. Ejecuta Descubrir primero y asegúrate de que la puerta de enlace responda ARP. No se inició el control."),
        ["MsgDriverError"] = ("Packet driver error: {0}", "Error del controlador de paquetes: {0}"),
        ["MsgNoGateway"] = ("The selected adapter has no default gateway, so traffic cannot be intercepted. Choose the adapter that connects to your router.",
                            "El adaptador seleccionado no tiene puerta de enlace predeterminada, así que no se puede interceptar el tráfico. Elige el adaptador que conecta con tu router."),
        ["MsgSendFailed"] = ("The capture opened but sending packets failed: {0}\n\nTraffic control will not take effect. Check that Npcap is installed correctly and that the adapter is connected.",
                             "La captura se abrió pero el envío de paquetes falló: {0}\n\nEl control de tráfico no funcionará. Verifica que Npcap esté bien instalado y que el adaptador esté conectado."),
        ["AboutDiagAdapter"] = ("\n\nAdapter: {0}\nLocal: {1} ({2})\nGateway: {3} ({4})",
                                "\n\nAdaptador: {0}\nLocal: {1} ({2})\nPuerta de enlace: {3} ({4})"),
        ["AboutDiagState"] = ("\nState: {0} · devices: {1} · targets: {2}\nForwarded: {3:N0} B down / {4:N0} B up\nDropped: {5:N0} · send failures: {6:N0}",
                              "\nEstado: {0} · dispositivos: {1} · objetivos: {2}\nReenviado: {3:N0} B bajada / {4:N0} B subida\nDescartado: {5:N0} · fallos de envío: {6:N0}"),
        ["MsgAbiError"] = ("Driver ABI mismatch detected: {0}. Traffic was not processed.",
                           "Se detectó una incompatibilidad de ABI del controlador: {0}. No se procesó el tráfico."),
        ["TotalTraffic"] = ("Total  ↓ {0}  ↑ {1}", "Total  ↓ {0}  ↑ {1}"),
        ["DevicesCount"] = ("Devices: {0}", "Dispositivos: {0}"),
        ["ChartTotal"] = ("Total bandwidth", "Ancho de banda total"),
        ["ChartSelected"] = ("Selected device", "Dispositivo seleccionado"),
        ["ChartDown"] = ("Down", "Descarga"),
        ["ChartUp"] = ("Up", "Subida"),
        ["NameColumnTooltip"] = ("Double-click a name to rename it. Names are remembered by MAC address.",
                                 "Doble clic en un nombre para cambiarlo. Los nombres se recuerdan por dirección MAC."),
        ["CapCellTooltip"] = ("Limit in {0}. 0 = unlimited.", "Límite en {0}. 0 = ilimitado."),
        ["SpoofColumnTooltip"] = ("Include this device when shaping is active (ARP poison target)",
                                  "Incluir este dispositivo cuando el control esté activo (objetivo del envenenamiento ARP)"),
        ["BlockColumnTooltip"] = ("Drop all traffic to/from this device",
                                  "Descartar todo el tráfico hacia/desde este dispositivo"),
        ["AboutText"] = ("SelfishNet v2 — modern revamp of the 2007 classic.\nDual-engine LAN traffic controller: ARP discovery, per-device shaping and blocking.\nEngines: Legacy (faithful replica) and Modern (token-bucket, zero-alloc).\n\nUse only on networks you own or are authorized to test.",
                         "SelfishNet v2 — rediseño moderno del clásico de 2007.\nControlador de tráfico LAN de doble motor: descubrimiento ARP, control por dispositivo y bloqueo.\nMotores: Legacy (réplica fiel) y Moderno (token-bucket, cero asignaciones).\n\nÚsalo solo en redes propias o autorizadas."),
    };

    public static string Get(string key, params object[] args)
    {
        if (!Strings.TryGetValue(key, out var pair))
            return key;
        string raw = CurrentLanguage == Language.English ? pair.En : pair.Es;
        return args.Length == 0 ? raw : string.Format(raw, args);
    }
}
