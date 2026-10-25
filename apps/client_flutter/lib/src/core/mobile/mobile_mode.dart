/// Zarr mobile operating modes.
enum MobileMode { standalone, companion }

/// Companion devices must never assume desktop authority without pairing.
class MobileConfiguration {
  const MobileConfiguration({
    required this.mode,
    this.serverUrl,
    this.companyId,
    this.deviceId,
    this.paired = false,
  });

  final MobileMode mode;
  final String? serverUrl;
  final String? companyId;
  final String? deviceId;
  final bool paired;

  bool get canSync =>
      mode == MobileMode.companion &&
      paired &&
      serverUrl != null &&
      serverUrl!.startsWith('https://') &&
      companyId != null &&
      deviceId != null;

  Map<String, Object?> toJson() => {
    'mode': mode.name,
    'serverUrl': serverUrl,
    'companyId': companyId,
    'deviceId': deviceId,
    'paired': paired,
  };

  factory MobileConfiguration.fromJson(Map<String, dynamic> json) {
    return MobileConfiguration(
      mode: json['mode'] == 'companion'
          ? MobileMode.companion
          : MobileMode.standalone,
      serverUrl: json['serverUrl'] as String?,
      companyId: json['companyId'] as String?,
      deviceId: json['deviceId'] as String?,
      paired: json['paired'] == true,
    );
  }
}
