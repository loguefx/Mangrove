package com.mangrove.app

import android.app.Application
import com.mangrove.app.data.AppContainer

class MangroveApp : Application() {
    lateinit var container: AppContainer
        private set

    override fun onCreate() {
        super.onCreate()
        container = AppContainer(this)
    }
}
