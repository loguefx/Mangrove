# kotlinx.serialization keeps generated serializers; rules below are standard.
-keepattributes *Annotation*, InnerClasses
-dontnote kotlinx.serialization.**
-keepclassmembers class **$$serializer { *; }
-keepclassmembers class com.mangrove.app.data.** {
    *** Companion;
}
-keepclasseswithmembers class com.mangrove.app.data.** {
    kotlinx.serialization.KSerializer serializer(...);
}
