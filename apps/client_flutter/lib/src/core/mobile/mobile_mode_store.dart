import 'dart:convert';

import '../database/local_database.dart';
import 'mobile_mode.dart';

/// Mode preference is local per install; never synchronize this preference.
class MobileModeStore {
  MobileModeStore(this.database);

  final LocalDatabase database;
  static const metaKey = 'zarr.mobile.configuration.v1';

  Future<MobileConfiguration> load() async {
    final raw = await database.getMeta(metaKey);
    if (raw == null || raw.isEmpty) {
      return const MobileConfiguration(mode: MobileMode.standalone);
    }
    try {
      return MobileConfiguration.fromJson(
        Map<String, dynamic>.from(jsonDecode(raw) as Map),
      );
    } on FormatException {
      return const MobileConfiguration(mode: MobileMode.standalone);
    } on TypeError {
      return const MobileConfiguration(mode: MobileMode.standalone);
    }
  }

  Future<void> save(MobileConfiguration configuration) async {
    await database.setMeta(metaKey, jsonEncode(configuration.toJson()));
  }
}
