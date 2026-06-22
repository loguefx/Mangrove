package com.mangrove.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Modifier
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import com.mangrove.app.data.AppContainer
import com.mangrove.app.ui.HomeScreen
import com.mangrove.app.ui.LibraryScreen
import com.mangrove.app.ui.LoadingBox
import com.mangrove.app.ui.LoginScreen
import com.mangrove.app.ui.ReaderScreen
import com.mangrove.app.ui.SeriesScreen
import com.mangrove.app.ui.ServerScreen
import com.mangrove.app.ui.theme.MangroveTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val container = (application as MangroveApp).container
        setContent {
            MangroveTheme {
                Surface(modifier = Modifier.fillMaxSize()) {
                    AppRoot(container)
                }
            }
        }
    }
}

@Composable
private fun AppRoot(container: AppContainer) {
    val nav = rememberNavController()
    NavHost(navController = nav, startDestination = "splash") {
        composable("splash") { Splash(container, nav) }
        composable("server") { ServerScreen(container, nav) }
        composable("login") { LoginScreen(container, nav) }
        composable("home") { HomeScreen(container, nav) }
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
