# Near-empty on purpose: kotlinx-serialization, supabase-kt, Ktor and OkHttp all ship
# consumer keep rules in their AARs/JARs. Rules are added here only when R8's own
# missing-rules output demands them — never speculatively.

# OkHttp probes optional TLS providers reflectively; none are on the classpath.
-dontwarn okhttp3.internal.platform.**
-dontwarn org.conscrypt.**
-dontwarn org.bouncycastle.**
-dontwarn org.openjsse.**

# Ktor references SLF4J on the JVM; Android builds have no binding and need none.
-dontwarn org.slf4j.**
