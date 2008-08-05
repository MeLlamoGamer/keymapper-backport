﻿using System;
using System.Collections.ObjectModel;
using System.Web.UI.WebControls;

namespace KMBlog
{
    public partial class edit_categories : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            newcategory.SetSaveAsDefaultButton();

            LoadCategories();
            if (AppController.IsUserAdmin(User) == false)
                newcategory.DisableSave();

            newcategory.CategorySaved += NewCategorySaved;

			ClientScript.RegisterClientScriptBlock(this.GetType(),
			"confirmButtonScript",
			"function __doConfirm(btn){if (confirm('Are you sure you wish to submit?')){return true;}else{return false;}}",
			true);

        }

        void NewCategorySaved(object sender, EventArgs e)
        {
            LoadCategories();
        }

        

        private void LoadCategories()
        {
            rptCategories.DataSource = null;
            Collection<Category> catlist = Category.GetAllCategories();
            rptCategories.DataSource = catlist;
            rptCategories.DataBind();
        }

        public void DeleteCategory(object sender, CommandEventArgs e)
        {
            if (AppController.IsUserAdmin(User) == false)
                return;

            int categoryID;
            if (Int32.TryParse(e.CommandArgument.ToString(), out categoryID) == false)
                return;

            Category.Delete(categoryID);

            LoadCategories();

        }

    }
}
