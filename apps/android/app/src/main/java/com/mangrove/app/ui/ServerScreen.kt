package com.mangrove.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import com.mangrove.app.data.AppContainer
import com.mangrove.app.ui.theme.BgDark
import com.mangrove.app.ui.theme.TealDeep
import com.mangrove.app.ui.theme.TealMint

@Composable
fun ServerScreen(container: AppContainer, nav: NavController) {
    var url by remember { mutableStateOf(container.baseUrl ?: "") }
    var error by remember { mutableStateOf<String?>(null) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(Brush.verticalGradient(listOf(BgDark, TealDeep)))
            .padding(24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text("Mangrove", fontSize = 28.sp, fontWeight = FontWeight.SemiBold)
        Text(
            "Connect to your server",
            color = TealMint,
            modifier = Modifier.padding(top = 4.dp, bottom = 24.dp),
        )
        OutlinedTextField(
            value = url,
            onValueChange = { url = it; error = null },
            label = { Text("Server URL") },
            placeholder = { Text("http://192.168.0.10:5000") },
            singleLine = true,
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
            modifier = Modifier.fillMaxWidth(),
        )
        if (error != null) {
            Text(error!!, color = androidx.compose.material3.MaterialTheme.colorScheme.error, modifier = Modifier.padding(top = 8.dp))
        }
        androidx.compose.foundation.layout.Spacer(Modifier.height(20.dp))
        Button(
            onClick = {
                try {
                    container.setServer(url)
                    nav.navigate("login") { popUpTo("server") { inclusive = true } }
                } catch (e: Exception) {
                    error = "Enter a valid URL like http://host:port"
                }
            },
            enabled = url.isNotBlank(),
            shape = RoundedCornerShape(14.dp),
            modifier = Modifier.fillMaxWidth().height(50.dp),
        ) {
            Text("Continue")
        }
    }
}
