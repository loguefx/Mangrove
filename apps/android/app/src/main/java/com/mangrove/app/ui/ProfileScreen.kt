package com.mangrove.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Person
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import androidx.navigation.NavGraph.Companion.findStartDestination
import com.mangrove.app.data.AppContainer
import com.mangrove.app.ui.theme.BgDark
import com.mangrove.app.ui.theme.Teal
import com.mangrove.app.ui.theme.TealDeep
import com.mangrove.app.ui.theme.TealMint
import kotlinx.coroutines.launch

@Composable
fun ProfileScreen(container: AppContainer, nav: NavController) {
    val user = container.user
    var wifiOnly by remember { mutableStateOf(container.prefs.wifiOnly) }
    val scope = rememberCoroutineScope()

    Column(
        Modifier
            .fillMaxSize()
            .background(Brush.verticalGradient(listOf(BgDark, TealDeep)))
            .verticalScroll(rememberScrollState()),
    ) {
        Box(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 16.dp)) {
            Text("Profile", fontSize = 24.sp, fontWeight = FontWeight.SemiBold)
        }

        // Account card.
        Column(
            Modifier
                .fillMaxWidth()
                .padding(horizontal = 16.dp)
                .clip(RoundedCornerShape(16.dp))
                .background(MaterialTheme.colorScheme.surface)
                .padding(20.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Box(
                Modifier.size(64.dp).clip(CircleShape).background(Teal),
                contentAlignment = Alignment.Center,
            ) {
                Icon(Icons.Filled.Person, contentDescription = null, tint = androidx.compose.ui.graphics.Color.White)
            }
            Spacer(Modifier.height(12.dp))
            Text(user?.username ?: "Signed in", fontSize = 20.sp, fontWeight = FontWeight.SemiBold)
            user?.email?.takeIf { it.isNotBlank() }?.let {
                Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            if (!user?.roles.isNullOrEmpty()) {
                Text(
                    user!!.roles.joinToString(" · "),
                    style = MaterialTheme.typography.labelMedium,
                    color = TealMint,
                    modifier = Modifier.padding(top = 4.dp),
                )
            }
        }

        SectionTitle("Settings")
        Column(
            Modifier
                .fillMaxWidth()
                .padding(horizontal = 16.dp)
                .clip(RoundedCornerShape(16.dp))
                .background(MaterialTheme.colorScheme.surface),
        ) {
            androidx.compose.foundation.layout.Row(
                Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 14.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column(Modifier.weight(1f)) {
                    Text("Download on Wi-Fi only")
                    Text(
                        "Pause downloads on mobile data",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                Switch(
                    checked = wifiOnly,
                    onCheckedChange = {
                        wifiOnly = it
                        container.prefs.wifiOnly = it
                        container.downloadManager.rescheduleForConstraintChange()
                    },
                )
            }
        }

        if (container.isAdmin) {
            SectionTitle("Administration")
            Column(
                Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp)
                    .clip(RoundedCornerShape(16.dp))
                    .background(MaterialTheme.colorScheme.surface)
                    .clickable { nav.navigate("admin") }
                    .padding(16.dp),
            ) {
                Text("Admin settings")
                Text(
                    "Auto-scan interval, libraries & folders, and users",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }

        SectionTitle("Server")
        Column(
            Modifier
                .fillMaxWidth()
                .padding(horizontal = 16.dp)
                .clip(RoundedCornerShape(16.dp))
                .background(MaterialTheme.colorScheme.surface)
                .padding(16.dp),
        ) {
            Text(
                container.baseUrl ?: "Not configured",
                style = MaterialTheme.typography.bodyMedium,
                color = TealMint,
            )
            Spacer(Modifier.height(12.dp))
            OutlinedButton(
                onClick = { nav.navigate("server") },
                shape = RoundedCornerShape(12.dp),
            ) { Text("Change server") }
        }

        Spacer(Modifier.height(24.dp))
        Button(
            onClick = {
                scope.launch {
                    container.logout()
                    nav.navigate("login") {
                        popUpTo(nav.graph.findStartDestination().id) { inclusive = true }
                    }
                }
            },
            colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.surface),
            shape = RoundedCornerShape(14.dp),
            modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp).height(50.dp),
        ) {
            Text("Sign out", color = MaterialTheme.colorScheme.error)
        }
        Spacer(Modifier.height(24.dp))
    }
}
