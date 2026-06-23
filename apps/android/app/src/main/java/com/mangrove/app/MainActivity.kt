package com.mangrove.app

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import androidx.compose.animation.AnimatedContentTransitionScope.SlideDirection
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.MenuBook
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.Person
import androidx.compose.material.icons.filled.Star
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.navigation.NavController
import androidx.navigation.NavGraph.Companion.findStartDestination
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import com.mangrove.app.data.AppContainer
import com.mangrove.app.ui.AdminScreen
import com.mangrove.app.ui.DownloadsScreen
import com.mangrove.app.ui.FavoritesScreen
import com.mangrove.app.ui.HomeScreen
import com.mangrove.app.ui.LibrariesScreen
import com.mangrove.app.ui.LibraryScreen
import com.mangrove.app.ui.LoadingBox
import com.mangrove.app.ui.LoginScreen
import com.mangrove.app.ui.ProfileScreen
import com.mangrove.app.ui.ReaderScreen
import com.mangrove.app.ui.SeriesScreen
import com.mangrove.app.ui.ServerScreen
import com.mangrove.app.ui.theme.MangroveTheme

class MainActivity : ComponentActivity() {
    private val notificationPermission =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { /* progress shows if granted */ }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val container = (application as MangroveApp).container

        // Ask for notification permission so download progress is visible (Android 13+).
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) !=
            PackageManager.PERMISSION_GRANTED
        ) {
            notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
        }

        setContent {
            MangroveTheme {
                Surface(modifier = Modifier.fillMaxSize()) {
                    AppRoot(container)
                }
            }
        }
    }
}

private data class TabItem(val route: String, val label: String, val icon: ImageVector)

private val TABS = listOf(
    TabItem("home", "Home", Icons.Filled.Home),
    TabItem("favorites", "Favorites", Icons.Filled.Star),
    TabItem("libraries", "Library", Icons.AutoMirrored.Filled.MenuBook),
    TabItem("downloads", "Downloads", Icons.Filled.Download),
    TabItem("profile", "Profile", Icons.Filled.Person),
)

private val TAB_ROUTES = TABS.map { it.route }.toSet()

@Composable
private fun AppRoot(container: AppContainer) {
    val nav = rememberNavController()
    val backStackEntry by nav.currentBackStackEntryAsState()
    val route = backStackEntry?.destination?.route
    // Only show the bar on top-level tabs and once the user is signed in (hidden on the
    // reader, detail screens, login/setup, and the offline-downloads-from-login path).
    val showBar = route in TAB_ROUTES && container.user != null

    Column(Modifier.fillMaxSize()) {
        Box(Modifier.weight(1f)) {
            NavHost(
                navController = nav,
                startDestination = "splash",
                enterTransition = { fadeIn(tween(220)) + slideIntoContainer(SlideDirection.Start, tween(220)) },
                exitTransition = { fadeOut(tween(180)) + slideOutOfContainer(SlideDirection.Start, tween(180)) },
                popEnterTransition = { fadeIn(tween(220)) + slideIntoContainer(SlideDirection.End, tween(220)) },
                popExitTransition = { fadeOut(tween(180)) + slideOutOfContainer(SlideDirection.End, tween(180)) },
            ) {
                composable("splash") { Splash(container, nav) }
                composable("server") { ServerScreen(container, nav) }
                composable("login") { LoginScreen(container, nav) }
                composable("home") { HomeScreen(container, nav) }
                composable("favorites") { FavoritesScreen(container, nav) }
                composable("libraries") { LibrariesScreen(container, nav) }
                composable("downloads") { DownloadsScreen(container, nav) }
                composable("profile") { ProfileScreen(container, nav) }
                composable("admin") { AdminScreen(container, nav) }
                composable(
                    "library/{id}",
                    arguments = listOf(navArgument("id") { type = NavType.IntType }),
                ) { entry ->
                    LibraryScreen(container, nav, entry.arguments!!.getInt("id"))
                }
                composable(
                    "series/{id}",
                    arguments = listOf(navArgument("id") { type = NavType.IntType }),
                ) { entry ->
                    SeriesScreen(container, nav, entry.arguments!!.getInt("id"))
                }
                composable(
                    "reader/{chapterId}",
                    arguments = listOf(navArgument("chapterId") { type = NavType.IntType }),
                ) { entry ->
                    ReaderScreen(container, nav, entry.arguments!!.getInt("chapterId"))
                }
            }
        }
        if (showBar) MangroveBottomBar(nav, route)
    }
}

@Composable
private fun MangroveBottomBar(nav: NavController, currentRoute: String?) {
    NavigationBar {
        TABS.forEach { tab ->
            NavigationBarItem(
                selected = currentRoute == tab.route,
                onClick = {
                    if (currentRoute != tab.route) {
                        nav.navigate(tab.route) {
                            popUpTo(nav.graph.findStartDestination().id) { saveState = true }
                            launchSingleTop = true
                            restoreState = true
                        }
                    }
                },
                icon = { Icon(tab.icon, contentDescription = tab.label) },
                label = { Text(tab.label) },
            )
        }
    }
}

@Composable
private fun Splash(container: AppContainer, nav: androidx.navigation.NavController) {
    LaunchedEffect(Unit) {
        val dest = when {
            !container.hasServer() -> "server"
            container.hasSession() && container.restoreSession() -> "home"
            else -> "login"
        }
        nav.navigate(dest) { popUpTo("splash") { inclusive = true } }
    }
    LoadingBox()
}
