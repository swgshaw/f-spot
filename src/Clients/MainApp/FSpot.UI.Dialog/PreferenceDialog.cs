/*
 * FSpot.UI.Dialog.PreferenceDialog.cs
 *
 * Authors(s):
 *	Larry Ewing  <lewing@novell.com>
 *	Stephane Delcroix  <stephane@delcroix.org>
 *
 * Copyright (c) 2005-2009 Novell, Inc.
 * Copyright (c) 2007,2010 Stephane Delcroix
 *
 * This is free software. See COPYING for details.
 */

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Gtk;
using Mono.Unix;
using Hyena;

using FSpot.Widgets;

namespace FSpot.UI.Dialog {
	public class PreferenceDialog : BuilderDialog
	{
		[GtkBeans.Builder.Object] FileChooserButton photosdir_chooser;

		[GtkBeans.Builder.Object] RadioButton writemetadata_radio;
		[GtkBeans.Builder.Object] RadioButton dontwrite_radio;
		[GtkBeans.Builder.Object] CheckButton always_sidecar_check;

		[GtkBeans.Builder.Object] ComboBox theme_combo;
		[GtkBeans.Builder.Object] ComboBox screenprofile_combo;
		[GtkBeans.Builder.Object] ComboBox printprofile_combo;

#region public API (ctor)
		public PreferenceDialog (Window parent) : base ("PreferenceDialog.ui", "preference_dialog")
		{
			TransientFor = parent;

			//Photos Folder
			if (FSpot.Core.Global.PhotoUri == new SafeUri (Preferences.Get<string> (Preferences.STORAGE_PATH))) {
				photosdir_chooser.SetCurrentFolderUri (FSpot.Core.Global.PhotoUri);
				photosdir_chooser.CurrentFolderChanged += HandlePhotosdirChanged;
			} else {
				photosdir_chooser.SetCurrentFolderUri (FSpot.Core.Global.PhotoUri);
				photosdir_chooser.Sensitive = false;
			}

			//Write Metadata
			LoadPreference (Preferences.METADATA_EMBED_IN_IMAGE);
			LoadPreference (Preferences.METADATA_ALWAYS_USE_SIDECAR);

			//Screen profile
			ListStore sprofiles = new ListStore (typeof (string), typeof (int));
			sprofiles.AppendValues (Catalog.GetString ("None"), 0);
			if (FSpot.ColorManagement.XProfile != null)
				sprofiles.AppendValues (Catalog.GetString ("System profile"), -1);
			sprofiles.AppendValues (null, 0);

			//Pick the display profiles from the full list, avoid _x_profile_
			var dprofs = from profile in FSpot.ColorManagement.Profiles
				where (profile.Value.DeviceClass == Cms.IccProfileClass.Display && profile.Key != "_x_profile_")
				select profile;
			foreach (var p in dprofs)
				sprofiles.AppendValues (p.Key, 1);

			CellRendererText profilecellrenderer = new CellRendererText ();
			profilecellrenderer.Ellipsize = Pango.EllipsizeMode.End;

			screenprofile_combo.Model = sprofiles;
			screenprofile_combo.PackStart (profilecellrenderer, true);
			screenprofile_combo.RowSeparatorFunc = ProfileSeparatorFunc;
			screenprofile_combo.SetCellDataFunc (profilecellrenderer, ProfileCellFunc);
			LoadPreference (Preferences.COLOR_MANAGEMENT_DISPLAY_PROFILE);

			//Print profile
			ListStore pprofiles = new ListStore (typeof (string), typeof (int));
			pprofiles.AppendValues (Catalog.GetString ("None"), 0);
			pprofiles.AppendValues (null, 0);

			var pprofs = from profile in FSpot.ColorManagement.Profiles
				where (profile.Value.DeviceClass == Cms.IccProfileClass.Output && profile.Key != "_x_profile_")
				select profile;
			foreach (var p in pprofs)
				pprofiles.AppendValues (p.Key, 1);

			printprofile_combo.Model = pprofiles;
			printprofile_combo.PackStart (profilecellrenderer, true);
			printprofile_combo.RowSeparatorFunc = ProfileSeparatorFunc;
			printprofile_combo.SetCellDataFunc (profilecellrenderer, ProfileCellFunc);
			LoadPreference (Preferences.COLOR_MANAGEMENT_OUTPUT_PROFILE);

			//Theme chooser
			ListStore themes = new ListStore (typeof (string), typeof (string));
			themes.AppendValues (Catalog.GetString ("Standard theme"), null);
			themes.AppendValues (null, null); //Separator
			string gtkrc = System.IO.Path.Combine ("gtk-2.0", "gtkrc");
			string [] search = {System.IO.Path.Combine (FSpot.Core.Global.HomeDirectory, ".themes"), "/usr/share/themes"};
			foreach (string path in search)
				if (System.IO.Directory.Exists (path))
					foreach (string dir in System.IO.Directory.GetDirectories (path))
						if (File.Exists (System.IO.Path.Combine (dir, gtkrc)))
							themes.AppendValues (System.IO.Path.GetFileName (dir), System.IO.Path.Combine (dir, gtkrc));
			CellRenderer themecellrenderer = new CellRendererText ();
			theme_combo.Model = themes;
			theme_combo.PackStart (themecellrenderer, true);
			theme_combo.RowSeparatorFunc = ThemeSeparatorFunc;
			theme_combo.SetCellDataFunc (themecellrenderer, ThemeCellFunc);
			LoadPreference (Preferences.GTK_RC);

			ConnectEvents ();
		}
#endregion

#region preferences
		void OnPreferencesChanged (object sender, NotifyEventArgs args)
		{
			LoadPreference (args.Key);
		}

		void LoadPreference (string key)
		{
			string pref;
			int i;
			switch (key) {
			case Preferences.STORAGE_PATH:
				photosdir_chooser.SetCurrentFolder (Preferences.Get<string> (key));
				break;
			case Preferences.METADATA_EMBED_IN_IMAGE:
				bool embed_active = Preferences.Get<bool> (key);
				if (writemetadata_radio.Active != embed_active) {
					if (embed_active) {
						writemetadata_radio.Active = true;
					} else {
						dontwrite_radio.Active = true;
					}
				}
				always_sidecar_check.Sensitive = embed_active;
				break;
			case Preferences.METADATA_ALWAYS_USE_SIDECAR:
				bool always_use_sidecar = Preferences.Get<bool> (key);
				always_sidecar_check.Active = always_use_sidecar;
				break;
			case Preferences.GTK_RC:
				pref = Preferences.Get<string> (key);
				if (String.IsNullOrEmpty (pref)) {
					theme_combo.Active = 0;
					break;
				}
				i = 0;
				foreach (object [] row in theme_combo.Model as ListStore) {
					if (pref == (string)row [1]) {
						theme_combo.Active = i;
						break;
					}
					i++;
				}
				break;
			case Preferences.COLOR_MANAGEMENT_DISPLAY_PROFILE:
				pref = Preferences.Get<string> (key);
				if (String.IsNullOrEmpty (pref)) {
					screenprofile_combo.Active = 0;
					break;
				}
				if (pref == "_x_profile_" && FSpot.ColorManagement.XProfile != null) {
					screenprofile_combo.Active = 1;
					break;
				}
				i = 0;
				foreach (object [] row in screenprofile_combo.Model as ListStore) {
					if (pref == (string)row [0]) {
						screenprofile_combo.Active = i;
						break;
					}
					i++;
				}
				break;
			case Preferences.COLOR_MANAGEMENT_OUTPUT_PROFILE:
				pref = Preferences.Get<string> (key);
				if (String.IsNullOrEmpty (pref)) {
					printprofile_combo.Active = 0;
					break;
				}
				i = 0;
				foreach (object [] row in printprofile_combo.Model as ListStore) {
					if (pref == (string)row [0]) {
						printprofile_combo.Active = i;
						break;
					}
					i++;
				}
				break;
			}
		}
#endregion

#region event handlers
		void ConnectEvents ()
		{
			Preferences.SettingChanged += OnPreferencesChanged;
			screenprofile_combo.Changed += HandleScreenProfileComboChanged;
			printprofile_combo.Changed += HandlePrintProfileComboChanged;
			theme_combo.Changed += HandleThemeComboChanged;
			writemetadata_radio.Toggled += HandleWritemetadataGroupChanged;
			always_sidecar_check.Toggled += HandleAlwaysSidecareCheckToggled;
		}

		void HandlePhotosdirChanged (object sender, System.EventArgs args)
		{
			photosdir_chooser.CurrentFolderChanged -= HandlePhotosdirChanged;
			Preferences.Set (Preferences.STORAGE_PATH, photosdir_chooser.Filename);
			photosdir_chooser.CurrentFolderChanged += HandlePhotosdirChanged;
			FSpot.Core.Global.PhotoUri = new SafeUri (photosdir_chooser.Uri, true);
		}

		void HandleWritemetadataGroupChanged (object sender, System.EventArgs args)
		{
			Preferences.Set (Preferences.METADATA_EMBED_IN_IMAGE, writemetadata_radio.Active);
		}

		void HandleAlwaysSidecareCheckToggled (object sender, EventArgs args)
		{
			Preferences.Set (Preferences.METADATA_ALWAYS_USE_SIDECAR, always_sidecar_check.Active);
		}

		void HandleThemeComboChanged (object sender, EventArgs e)
		{
			ComboBox combo = sender as ComboBox;
			if (combo == null)
				return;
			TreeIter iter;
			if (combo.GetActiveIter (out iter)) {
				string gtkrc = (string)combo.Model.GetValue (iter, 1);
				if (!String.IsNullOrEmpty (gtkrc))
					Preferences.Set (Preferences.GTK_RC, gtkrc);
				else
					Preferences.Set (Preferences.GTK_RC, String.Empty);
			}
			Gtk.Rc.DefaultFiles = FSpot.Core.Global.DefaultRcFiles;
			Gtk.Rc.AddDefaultFile (Preferences.Get<string> (Preferences.GTK_RC));
			Gtk.Rc.ReparseAllForSettings (Gtk.Settings.Default, true);
		}

		void HandleScreenProfileComboChanged (object sender, EventArgs e)
		{
			ComboBox combo = sender as ComboBox;
			if (combo == null)
				return;
			TreeIter iter;
			if (combo.GetActiveIter (out iter)) {
				switch ((int)combo.Model.GetValue (iter, 1)) {
				case 0:
					Preferences.Set (Preferences.COLOR_MANAGEMENT_DISPLAY_PROFILE, String.Empty);
					break;
				case -1:
					Preferences.Set (Preferences.COLOR_MANAGEMENT_DISPLAY_PROFILE, "_x_profile_");
					break;
				case 1:
					Preferences.Set (Preferences.COLOR_MANAGEMENT_DISPLAY_PROFILE, (string)combo.Model.GetValue (iter, 0));
					break;
				}
			}
		}

		void HandlePrintProfileComboChanged (object sender, EventArgs e)
		{
			ComboBox combo = sender as ComboBox;
			if (combo == null)
				return;
			TreeIter iter;
			if (combo.GetActiveIter (out iter)) {
				switch ((int)combo.Model.GetValue (iter, 1)) {
				case 0:
					Preferences.Set (Preferences.COLOR_MANAGEMENT_OUTPUT_PROFILE, String.Empty);
					break;
				case 1:
					Preferences.Set (Preferences.COLOR_MANAGEMENT_OUTPUT_PROFILE, (string)combo.Model.GetValue (iter, 0));
					break;
				}
			}
		}
#endregion

#region Gtk widgetry
		void ThemeCellFunc (CellLayout cell_layout, CellRenderer cell, TreeModel tree_model, TreeIter iter)
		{
			string name = (string)tree_model.GetValue (iter, 0);
			(cell as CellRendererText).Text = name;
		}

		bool ThemeSeparatorFunc (TreeModel tree_model, TreeIter iter)
		{
			return tree_model.GetValue (iter, 0) == null;
		}

		void ProfileCellFunc (CellLayout cell_layout, CellRenderer cell, TreeModel tree_model, TreeIter iter)
		{
			string name = (string)tree_model.GetValue (iter, 0);
			(cell as CellRendererText).Text = name;
		}

		bool ProfileSeparatorFunc (TreeModel tree_model, TreeIter iter)
		{
			return tree_model.GetValue (iter, 0) == null;
		}
#endregion
	}
}