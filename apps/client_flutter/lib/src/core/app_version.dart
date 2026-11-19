/// Single source of truth for the Zarr client version shown in the UI.
/// Keep this synchronized with the version field in pubspec.yaml.
abstract final class ZarrAppVersion {
  static const String name = '0.5.0-beta.8';
  static const int build = 12;
  static const String display = '0.5.0-beta.8 (build 12)';
  static const String platforms = 'Windows • Android';
}
